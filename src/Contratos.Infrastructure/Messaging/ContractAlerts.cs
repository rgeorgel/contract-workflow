using Contratos.Application.Email;
using Contratos.Domain.Contracts;
using Contratos.Domain.Users;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Messaging;

public static class ContractAlertKinds
{
    /// <summary>Esperando assinatura há mais dias que o configurado.</summary>
    public const string Stale = "stale";
    /// <summary>O prazo para assinar está perto.</summary>
    public const string Expiring = "expiring";
}

public sealed record ContractAlertItem(Guid ContractId, string Kind);
public sealed record ContractAlertMessage(Guid UserId, IReadOnlyList<ContractAlertItem> Items);

/// <summary>
/// Contratos que precisam de atenção: parados há dias ou com o prazo chegando. Cada contrato gera no máximo
/// um aviso de cada tipo por envio, e quem recebe ganha um e-mail só, com todos os seus contratos da rodada.
/// Junto com o aviso de prazo, o link pode ser reenviado a quem ainda não assinou (configurável).
/// </summary>
public sealed class ContractAlerts(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<WorkerOptions> options,
    ILogger<ContractAlerts> logger)
{
    private const int Batch = 500;
    private DateTimeOffset _nextRun = DateTimeOffset.MinValue;

    public async Task RunIfDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (now < _nextRun)
            return;
        _nextRun = now.AddMinutes(Math.Max(0, options.Value.AlertIntervalMinutes));

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Candidatos amplos no banco (o máximo de dias é 60); o corte por organização é feito aqui.
        var horizon = now.AddDays(Domain.Organizations.Organization.MaxAlertDays);
        var candidates = await db.Contracts.IgnoreQueryFilters().Include(c => c.Signers)
            .Where(c => (c.Status == ContractStatus.Sent || c.Status == ContractStatus.PartiallySigned)
                && c.AutentiqueDocumentId != null
                && ((c.StaleAlertedAt == null && c.SentAt != null)
                    || (c.ExpiryAlertedAt == null && c.ExpiresAt != null && c.ExpiresAt <= horizon)))
            .OrderBy(c => c.SentAt)
            .Take(Batch)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return;

        var orgIds = candidates.Select(c => c.OrganizationId).Distinct().ToList();
        var settings = await db.Organizations.Where(o => orgIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Settings, ct);

        var alerts = new List<(Contract Contract, string Kind)>();
        foreach (var c in candidates)
        {
            var s = settings[c.OrganizationId];
            if (c.StaleAlertedAt is null && s.StaleAlertDays > 0 && c.SentAt <= now.AddDays(-s.StaleAlertDays))
            {
                c.MarkStaleAlerted(now);
                alerts.Add((c, ContractAlertKinds.Stale));
            }
            if (c.ExpiryAlertedAt is null && s.ExpiryAlertDays > 0 && c.ExpiresAt is { } exp && exp <= now.AddDays(s.ExpiryAlertDays))
            {
                c.MarkExpiryAlerted(now);
                alerts.Add((c, ContractAlertKinds.Expiring));
                if (s.AutoResendBeforeExpiry)
                    foreach (var signer in c.PendingSigners())
                    {
                        db.ContractEvents.Add(new ContractEvent(c.Id, ContractEventTypes.LinkResent, signer.Id, null,
                            new() { ["channel"] = signer.DeliveryMethod.ToString(), ["auto"] = "true" }));
                        db.Enqueue(OutboxTypes.ResendSignature, new ResendMessage(c.Id, signer.Id));
                    }
            }
        }
        if (alerts.Count == 0)
            return;

        // Quem recebe: quem criou o contrato, se ainda tem acesso; senão, os Admins da organização.
        var creatorIds = alerts.Select(a => a.Contract.CreatedBy).Distinct().ToList();
        var activeCreators = await db.Users
            .Where(u => creatorIds.Contains(u.Id) && u.DeactivatedAt == null && u.PasswordHash != null)
            .Select(u => u.Id).ToListAsync(ct);
        var adminsByOrg = await db.Users
            .Where(u => orgIds.Contains(u.OrganizationId) && u.Role == UserRole.Admin && u.DeactivatedAt == null && u.PasswordHash != null)
            .Select(u => new { u.OrganizationId, u.Id }).ToListAsync(ct);

        var perUser = new Dictionary<Guid, List<ContractAlertItem>>();
        foreach (var (contract, kind) in alerts)
        {
            var recipients = activeCreators.Contains(contract.CreatedBy)
                ? [contract.CreatedBy]
                : adminsByOrg.Where(a => a.OrganizationId == contract.OrganizationId).Select(a => a.Id).ToList();
            foreach (var userId in recipients)
            {
                if (!perUser.TryGetValue(userId, out var items))
                    perUser[userId] = items = [];
                items.Add(new ContractAlertItem(contract.Id, kind));
            }
        }
        foreach (var (userId, items) in perUser)
            db.Enqueue(OutboxTypes.ContractAlert, new ContractAlertMessage(userId, items));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Alertas de contrato: {Count} avisos para {Users} pessoas", alerts.Count, perUser.Count);
    }

    /// <summary>Monta e envia o e-mail de um aviso guardado na outbox.</summary>
    internal static async Task SendAsync(IServiceProvider services, AppDbContext db, ContractAlertMessage msg, CancellationToken ct)
    {
        var user = await db.Users.Where(u => u.Id == msg.UserId && u.DeactivatedAt == null)
            .Select(u => new { u.Email, u.Name }).SingleOrDefaultAsync(ct);
        if (user?.Email is null)
            return;

        var ids = msg.Items.Select(i => i.ContractId).Distinct().ToList();
        var contracts = await db.Contracts.IgnoreQueryFilters().Include(c => c.Signers)
            .Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var app = services.GetRequiredService<IOptions<AppOptions>>().Value;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var baseUrl = app.PublicBaseUrl.TrimEnd('/');

        var lines = new List<ContractEmails.AttentionLine>();
        foreach (var item in msg.Items)
        {
            // Assinado ou cancelado antes de o aviso sair: não precisa mais de atenção.
            if (!contracts.TryGetValue(item.ContractId, out var c) || !c.IsOpen)
                continue;
            var pending = c.Signers.Where(s => s.Status is not (SignerStatus.Signed or SignerStatus.Declined))
                .OrderBy(s => s.SignOrder).Select(s => s.Name).ToList();
            var reason = item.Kind == ContractAlertKinds.Expiring && c.ExpiresAt is { } exp
                ? ContractEmails.ExpiringReason(exp, now)
                : ContractEmails.StaleReason(c.SentAt ?? now, now);
            lines.Add(new ContractEmails.AttentionLine(c.Number, c.Title, reason, pending, $"{baseUrl}/contract.html?id={c.Id}"));
        }
        if (lines.Count == 0)
            return;
        await services.GetRequiredService<IEmailSender>().SendAsync(
            ContractEmails.NeedsAttention(app.ProductName, user.Email, user.Name, lines, $"{baseUrl}/contracts.html"), ct);
    }
}

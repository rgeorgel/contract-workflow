using Contratos.Application.Email;
using Contratos.Application.Signatures;
using Contratos.Application.Storage;
using Contratos.Domain.Contracts;
using Contratos.Infrastructure.Persistence;
using Contratos.Infrastructure.Signatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Messaging;

public sealed class WorkerOptions
{
    public const string Section = "Worker";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 2;
    /// <summary>Espera entre tentativas; vazio usa 1, 5, 15 e 60 min. Depois da última, desiste.</summary>
    /// <remarks>Sem valor inicial: o binder de configuração somaria os itens configurados aos padrões.</remarks>
    public int[]? RetryDelaysSeconds { get; set; }

    public int[] Delays => RetryDelaysSeconds is { Length: > 0 } d ? d : [60, 300, 900, 3600];

    /// <summary>De quanto em quanto tempo procurar contratos que podem ter perdido webhooks.</summary>
    public int ReconcileIntervalMinutes { get; set; } = 30;
    /// <summary>Contrato enviado (ou consultado) há mais que isso, ainda em aberto, é consultado no provedor.</summary>
    public int ReconcileAfterHours { get; set; } = 24;
    /// <summary>Consultas por rodada: a Autentique aceita 60 requisições por minuto para a plataforma inteira.</summary>
    public int ReconcileBatch { get; set; } = 20;
}

/// <summary>Recebe webhooks: valida a assinatura e grava em webhook_inbox; o processamento é assíncrono.</summary>
public sealed class WebhookReceiver(AppDbContext db, ISignatureProvider provider, ILogger<WebhookReceiver> logger)
{
    public enum Outcome { Accepted, InvalidSignature, Malformed }

    public async Task<Outcome> ReceiveAsync(string rawBody, string? signatureHeader, CancellationToken ct)
    {
        if (!provider.ValidateWebhook(rawBody, signatureHeader))
            return Outcome.InvalidSignature;

        IReadOnlyList<SignatureEvent> events;
        try
        {
            events = provider.ParseWebhook(rawBody);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or FormatException or InvalidOperationException)
        {
            logger.LogWarning(e, "Webhook malformado de {Provider}", provider.Name);
            return Outcome.Malformed;
        }
        if (events.Count == 0)
            return Outcome.Accepted;

        // Idempotência: o mesmo event.id reentregue é ignorado pelo índice único.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO webhook_inbox (provider, external_event_id, payload, received_at, attempts)
            VALUES ({provider.Name}, {events[0].ExternalEventId}, {rawBody}::jsonb, now(), 0)
            ON CONFLICT (provider, external_event_id) DO NOTHING
            """, ct);
        return Outcome.Accepted;
    }
}

/// <summary>
/// Processa, nesta ordem: webhooks recebidos, mensagens da outbox (chamadas ao provedor com retentativa)
/// e contratos vencidos. O worker chama em laço; os testes chamam direto.
/// </summary>
public sealed class BackgroundProcessor(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<WorkerOptions> options,
    ProviderBalanceMonitor balance,
    ILogger<BackgroundProcessor> logger)
{
    private const int BatchLimit = 50;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private DateTimeOffset _nextReconcile = DateTimeOffset.MinValue;

    public async Task ProcessAllAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            for (var i = 0; i < BatchLimit && await ProcessNextInboxAsync(ct); i++) { }
            for (var i = 0; i < BatchLimit && await ProcessNextOutboxAsync(ct); i++) { }
            await ExpireContractsAsync(ct);
            await ReconcileAsync(ct);
            await balance.CheckIfDueAsync(ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    // ---------- Webhooks ----------

    private async Task<bool> ProcessNextInboxAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var provider = scope.ServiceProvider.GetRequiredService<ISignatureProvider>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var item = (await db.WebhookInbox
            .FromSql($"SELECT * FROM webhook_inbox WHERE processed_at IS NULL ORDER BY id LIMIT 1 FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct)).SingleOrDefault();
        if (item is null)
            return false;

        try
        {
            if (item.Provider == Billing.Stripe.StripeWebhookReceiver.ProviderName)
                await scope.ServiceProvider.GetRequiredService<Billing.Stripe.StripeSubscriptionSync>().HandleEventAsync(item.Payload, ct);
            else
                foreach (var ev in provider.ParseWebhook(item.Payload))
                    await ApplyEventAsync(db, ev, ct);
            item.ProcessedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await tx.RollbackAsync(ct);
            logger.LogError(e, "Falha ao processar webhook {InboxId}", item.Id);
            await RecordInboxFailureAsync(item.Id, e.Message, ct);
        }
        return true;
    }

    private async Task RecordInboxFailureAsync(long id, string error, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.WebhookInbox.SingleAsync(w => w.Id == id, ct);
        item.Attempts++;
        item.LastError = Truncate(error);
        if (item.Attempts >= options.Value.Delays.Length + 1)
            item.ProcessedAt = clock.GetUtcNow(); // desiste; o erro fica registrado
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyEventAsync(AppDbContext db, SignatureEvent ev, CancellationToken ct)
    {
        // Webhook não tem tenant: acha o contrato (e a organização) pelo id do documento,
        // ou pela assinatura quando o evento não traz o documento.
        var contract = !string.IsNullOrEmpty(ev.DocumentId)
            ? await db.Contracts.IgnoreQueryFilters().Include(c => c.Signers)
                .SingleOrDefaultAsync(c => c.AutentiqueDocumentId == ev.DocumentId, ct)
            : ev.SignaturePublicId is { } publicId
                ? await db.Contracts.IgnoreQueryFilters().Include(c => c.Signers)
                    .SingleOrDefaultAsync(c => c.Signers.Any(s => s.AutentiquePublicId == publicId), ct)
                : null;
        if (contract is null)
        {
            logger.LogWarning("Webhook para documento desconhecido {DocumentId} {SignatureId}", ev.DocumentId, ev.SignaturePublicId);
            return;
        }

        var signer = ev.SignaturePublicId is null ? null : contract.Signers.SingleOrDefault(s => s.AutentiquePublicId == ev.SignaturePublicId);
        void Log(string type, Dictionary<string, string?>? payload = null) =>
            db.ContractEvents.Add(new ContractEvent(contract.Id, type, signer?.Id, payload: payload, occurredAt: ev.OccurredAt));

        switch (ev.Type)
        {
            case SignatureEventType.Viewed when signer is not null:
                if (contract.SignerViewed(signer, ev.OccurredAt)) Log(ContractEventTypes.SignerViewed);
                break;
            case SignatureEventType.Signed when signer is not null:
                if (contract.SignerSigned(signer, ev.OccurredAt)) Log(ContractEventTypes.SignerSigned);
                break;
            case SignatureEventType.Declined when signer is not null:
                if (contract.SignerDeclined(signer, ev.OccurredAt, ev.Reason)) Log(ContractEventTypes.SignerDeclined, new() { ["reason"] = ev.Reason });
                break;
            case SignatureEventType.DeliveryFailed when signer is not null:
                if (contract.SignerDeliveryFailed(signer)) Log(ContractEventTypes.DeliveryFailed, new() { ["channel"] = signer.DeliveryMethod.ToString() });
                break;
            case SignatureEventType.DocumentFinished:
                if (contract.Complete(ev.OccurredAt))
                {
                    Log(ContractEventTypes.Completed);
                    db.Enqueue(OutboxTypes.StoreSignedDocument, new ContractMessage(contract.Id));
                }
                break;
            case SignatureEventType.DocumentDeleted:
                // Exclusão pedida por nós já deixou o contrato cancelado; o resto é alerta.
                if (contract.Status != ContractStatus.Cancelled)
                    Log(ContractEventTypes.ProviderDocumentDeleted);
                break;
            default:
                logger.LogWarning("Evento {Type} sem signatário conhecido no contrato {ContractId}", ev.Type, contract.Id);
                break;
        }
    }

    // ---------- Outbox ----------

    private async Task<bool> ProcessNextOutboxAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        long messageId;
        string type;
        int attempts;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var message = (await db.OutboxMessages
                .FromSql($"SELECT * FROM outbox_messages WHERE processed_at IS NULL AND next_attempt_at <= {now} ORDER BY id LIMIT 1 FOR UPDATE SKIP LOCKED")
                .ToListAsync(ct)).SingleOrDefault();
            if (message is null)
                return false;

            (messageId, type, attempts) = (message.Id, message.Type, message.Attempts);
            try
            {
                await HandleAsync(scope.ServiceProvider, db, message, ct);
                message.ProcessedAt = clock.GetUtcNow();
                message.LastError = null;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return true;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                await tx.RollbackAsync(ct);
                logger.LogWarning(e, "Falha na mensagem {Type} #{Id} (tentativa {Attempt})", type, messageId, attempts + 1);
                await RecordOutboxFailureAsync(messageId, e, ct);
                if (e is SignatureProviderException { OutOfCredits: true })
                    await balance.ReportOutOfCreditsAsync(ct);
                return true;
            }
        }
    }

    private async Task RecordOutboxFailureAsync(long id, Exception failure, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = await db.OutboxMessages.SingleAsync(m => m.Id == id, ct);
        var delays = options.Value.Delays;
        var error = failure.Message;
        message.Attempts++;
        message.LastError = Truncate(error);

        // Dados recusados pelo provedor: repetir dá o mesmo erro; vai direto para "Erro no envio".
        var permanent = failure is SignatureProviderException { Permanent: true };
        if (permanent || message.Attempts > delays.Length)
        {
            // Esgotou (5 tentativas) ou não adianta tentar: desiste e deixa o erro visível no contrato.
            message.ProcessedAt = clock.GetUtcNow();
            if (message.Type == OutboxTypes.SendContract)
            {
                var contractId = message.Read<ContractMessage>().ContractId;
                var contract = await db.Contracts.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.Id == contractId, ct);
                if (contract is not null)
                {
                    contract.RecordSendFailure(error);
                    db.ContractEvents.Add(new ContractEvent(contract.Id, ContractEventTypes.SendFailed,
                        payload: new() { ["error"] = Truncate(error, 300) }));
                }
            }
        }
        else
        {
            message.NextAttemptAt = clock.GetUtcNow().AddSeconds(delays[message.Attempts - 1]);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task HandleAsync(IServiceProvider services, AppDbContext db, OutboxMessage message, CancellationToken ct)
    {
        var provider = services.GetRequiredService<ISignatureProvider>();
        var storage = services.GetRequiredService<IFileStorage>();

        switch (message.Type)
        {
            case OutboxTypes.SendContract:
            {
                var contract = await LoadContractAsync(db, message.Read<ContractMessage>().ContractId, ct);
                // Cancelado ou de volta a rascunho antes de sair, excluído ou já entregue: nada a fazer.
                if (contract is null || contract.Status != ContractStatus.Sent || contract.AutentiqueDocumentId is not null)
                    return;

                var org = await db.Organizations.SingleAsync(o => o.Id == contract.OrganizationId, ct);
                await using var pdf = await storage.OpenReadAsync(contract.DocumentKey!, ct)
                    ?? throw new InvalidOperationException("PDF original não encontrado no storage.");

                var signers = contract.SignersInSendOrder();
                var request = new DocumentRequest(
                    Name: $"{contract.Title} (nº {contract.Number:0000})",
                    // O signatário vê o nome da conta da plataforma; a mensagem diz quem está pedindo a assinatura.
                    Message: $"{org.Name} enviou este documento para você assinar." + (contract.Message is { } m ? $"\n\n{m}" : ""),
                    Sortable: contract.SigningMode == SigningMode.Sequential,
                    DeadlineAt: contract.ExpiresAt,
                    Reminder: org.Settings.Reminder,
                    FolderId: org.AutentiqueFolderId,
                    Signers: signers.Select(s => new DocumentSigner(s.Name, s.Email, s.Phone, s.DeliveryMethod, s.Action, s.Cpf)).ToList());

                var created = await provider.CreateDocumentAsync(request, pdf, ct);
                contract.AttachProviderDocument(created.DocumentId, created.Signatures.Select(s => (s.PublicId, s.ShortLink)).ToList());
                db.ContractEvents.Add(new ContractEvent(contract.Id, ContractEventTypes.SentToProvider,
                    payload: new() { ["provider"] = provider.Name, ["documentId"] = created.DocumentId }));
                break;
            }
            case OutboxTypes.DeleteProviderDocument:
                await provider.DeleteDocumentAsync(message.Read<ProviderDocumentMessage>().DocumentId, ct);
                break;
            case OutboxTypes.ResendSignature:
            {
                var msg = message.Read<ResendMessage>();
                var contract = await LoadContractAsync(db, msg.ContractId, ct);
                var signer = contract?.Signers.SingleOrDefault(s => s.Id == msg.SignerId);
                if (contract is { IsOpen: true } && signer?.AutentiquePublicId is { } publicId)
                    await provider.ResendAsync([publicId], ct);
                break;
            }
            case OutboxTypes.StoreSignedDocument:
            {
                var contract = await LoadContractAsync(db, message.Read<ContractMessage>().ContractId, ct);
                if (contract?.AutentiqueDocumentId is null || contract.SignedDocumentKey is not null)
                    return;
                await using var signedPdf = await provider.DownloadSignedPdfAsync(contract.AutentiqueDocumentId, ct);
                var key = StorageKeys.Signed(contract.OrganizationId, contract.Id);
                await storage.PutAsync(key, signedPdf, "application/pdf", ct);
                contract.AttachSignedDocument(key);
                db.ContractEvents.Add(new ContractEvent(contract.Id, ContractEventTypes.SignedDocumentStored));
                break;
            }
            case OutboxTypes.CreditsAlert:
            {
                var msg = message.Read<CreditsAlertMessage>();
                var org = await db.Organizations.SingleOrDefaultAsync(o => o.Id == msg.OrganizationId, ct);
                if (org is null)
                    break;
                var app = services.GetRequiredService<IOptions<AppOptions>>().Value;
                var email = services.GetRequiredService<IEmailSender>();
                var link = $"{app.PublicBaseUrl.TrimEnd('/')}/settings.html#pacotes";
                foreach (var admin in await ActiveAdminsAsync(db, org.Id, ct))
                    await email.SendAsync(AccountEmails.CreditsRunningOut(app.ProductName, admin.Email, admin.Name, org.Name, msg.Remaining, link), ct);
                break;
            }
            case OutboxTypes.PaymentOverdueAlert:
                await SendPaymentOverdueAlertAsync(services, db, message.Read<PaymentOverdueMessage>(), ct);
                break;
            case OutboxTypes.UsageAlert:
                await SendUsageAlertAsync(services, db, message.Read<UsageAlertMessage>(), ct);
                break;
            default:
                logger.LogError("Tipo de mensagem desconhecido: {Type}", message.Type);
                break;
        }
    }

    /// <summary>E-mail aos Admins ativos: 80% ou 100% da cota do plano no ciclo.</summary>
    private static async Task SendUsageAlertAsync(IServiceProvider services, AppDbContext db, UsageAlertMessage msg, CancellationToken ct)
    {
        var org = await db.Organizations.SingleOrDefaultAsync(o => o.Id == msg.OrganizationId, ct);
        if (org is null)
            return;
        var admins = await ActiveAdminsAsync(db, org.Id, ct);
        if (admins.Count == 0)
            return;

        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var credits = await db.CreditGrants
            .Where(g => g.OrganizationId == org.Id && g.Remaining > 0 && g.ExpiresAt > now)
            .SumAsync(g => (int?)g.Remaining, ct) ?? 0;
        var app = services.GetRequiredService<IOptions<AppOptions>>().Value;
        var email = services.GetRequiredService<IEmailSender>();
        var link = $"{app.PublicBaseUrl.TrimEnd('/')}/settings.html";
        var planName = Domain.Billing.Plans.Get(msg.PlanId).Name;
        foreach (var admin in admins)
            await email.SendAsync(AccountEmails.PlanUsage(app.ProductName, admin.Email, admin.Name, org.Name,
                planName, msg.Used, msg.Limit, credits, msg.PeriodEnd, link), ct);
    }

    private static async Task SendPaymentOverdueAlertAsync(IServiceProvider services, AppDbContext db, PaymentOverdueMessage msg, CancellationToken ct)
    {
        var org = await db.Organizations.SingleOrDefaultAsync(o => o.Id == msg.OrganizationId, ct);
        if (org is not { IsPaymentOverdue: true })
            return; // já foi pago antes de o aviso sair
        var app = services.GetRequiredService<IOptions<AppOptions>>().Value;
        var email = services.GetRequiredService<IEmailSender>();
        var link = $"{app.PublicBaseUrl.TrimEnd('/')}/settings.html";
        foreach (var admin in await ActiveAdminsAsync(db, org.Id, ct))
            await email.SendAsync(AccountEmails.PaymentOverdue(app.ProductName, admin.Email, admin.Name, org.Name, org.Plan.Name, link), ct);
    }

    private static async Task<List<(string Email, string Name)>> ActiveAdminsAsync(AppDbContext db, Guid organizationId, CancellationToken ct) =>
        (await db.Users
            .Where(u => u.OrganizationId == organizationId && u.Role == Domain.Users.UserRole.Admin
                && u.DeactivatedAt == null && u.PasswordHash != null && u.EmailConfirmed)
            .Select(u => new { u.Email, u.Name })
            .ToListAsync(ct))
        .Select(u => (u.Email!, u.Name)).ToList();

    // ---------- Expiração ----------

    private async Task ExpireContractsAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow();
        var expired = await db.Contracts.IgnoreQueryFilters()
            .Where(c => (c.Status == ContractStatus.Sent || c.Status == ContractStatus.PartiallySigned) && c.ExpiresAt != null && c.ExpiresAt <= now)
            .Take(BatchLimit)
            .ToListAsync(ct);
        foreach (var contract in expired.Where(c => c.Expire(now)))
            db.ContractEvents.Add(new ContractEvent(contract.Id, ContractEventTypes.Expired));
        if (expired.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    // ---------- Reconciliação ----------

    /// <summary>
    /// A Autentique tenta entregar um webhook 4 vezes em ~8 min. Para não depender só disso, contratos em aberto
    /// há mais de um dia são consultados na API (no máximo um lote por rodada) e os eventos que faltaram são aplicados.
    /// </summary>
    private async Task ReconcileAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (now < _nextReconcile)
            return;
        var o = options.Value;
        _nextReconcile = now.AddMinutes(Math.Max(0, o.ReconcileIntervalMinutes)); // 0: toda rodada (testes)
        var cutoff = now.AddHours(-o.ReconcileAfterHours);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var provider = scope.ServiceProvider.GetRequiredService<ISignatureProvider>();
        var due = await db.Contracts.IgnoreQueryFilters()
            .Where(c => (c.Status == ContractStatus.Sent || c.Status == ContractStatus.PartiallySigned)
                && c.AutentiqueDocumentId != null && c.SentAt < cutoff
                && (c.ProviderCheckedAt == null || c.ProviderCheckedAt < cutoff))
            .OrderBy(c => c.ProviderCheckedAt ?? c.SentAt)
            .Select(c => c.Id)
            .Take(Math.Max(1, o.ReconcileBatch))
            .ToListAsync(ct);

        foreach (var id in due)
        {
            var contract = await LoadContractAsync(db, id, ct);
            if (contract?.AutentiqueDocumentId is not { } documentId)
                continue;
            try
            {
                var status = await provider.GetDocumentAsync(documentId, ct);
                foreach (var ev in status.Events.OrderBy(e => e.OccurredAt))
                    await ApplyEventAsync(db, ev, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Reconciliação do contrato {ContractId} falhou", id);
            }
            contract.MarkProviderChecked(now);
            await db.SaveChangesAsync(ct);
        }
    }

    private static Task<Contract?> LoadContractAsync(AppDbContext db, Guid id, CancellationToken ct) =>
        db.Contracts.IgnoreQueryFilters().Include(c => c.Signers).SingleOrDefaultAsync(c => c.Id == id, ct);

    private static string Truncate(string s, int max = 2000) => s.Length > max ? s[..max] : s;
}

/// <summary>Chama o processador a cada poucos segundos. Desligável (testes chamam o processador direto).</summary>
internal sealed class BackgroundWorker(BackgroundProcessor processor, IOptions<WorkerOptions> options, ILogger<BackgroundWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds)));
        do
        {
            try
            {
                await processor.ProcessAllAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Banco fora do ar, por exemplo: tenta de novo no próximo ciclo.
                logger.LogError(e, "Falha no ciclo do worker");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

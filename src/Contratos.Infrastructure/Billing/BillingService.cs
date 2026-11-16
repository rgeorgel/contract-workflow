using Contratos.Application.Billing;
using Contratos.Application.Common;
using Contratos.Domain.Billing;
using Contratos.Domain.Organizations;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Billing;

/// <summary>Monta o quadro de plano e uso de uma organização (com ou sem tenant).</summary>
internal sealed class BillingReader(AppDbContext db, UsageMeter meter, TimeProvider clock, IOptions<BillingOptions> options, IOptions<Stripe.StripeOptions> stripe)
{
    public async Task<BillingDto> BuildAsync(Organization org, CancellationToken ct)
    {
        var snapshot = await meter.SnapshotAsync(org, ct);
        var now = clock.GetUtcNow();
        var grants = await db.CreditGrants
            .Where(g => g.OrganizationId == org.Id && g.Remaining > 0 && g.ExpiresAt > now)
            .OrderBy(g => g.ExpiresAt).ThenBy(g => g.CreatedAt)
            .Select(g => new CreditGrantDto(g.Id, g.Quantity, g.Remaining, g.CreatedAt, g.ExpiresAt, g.Source, g.Note))
            .ToListAsync(ct);
        var plan = snapshot.Plan;
        return new BillingDto(
            PlanDto.From(plan), snapshot.PeriodStart, snapshot.PeriodEnd,
            snapshot.PlanUsed, plan.ContractsPerPeriod, snapshot.CreditUsed,
            snapshot.CreditsAvailable, snapshot.AvailableSends,
            await meter.CountSeatsAsync(org.Id, ct), plan.MaxUsers,
            await meter.CountTemplatesAsync(org.Id, ct), plan.MaxTemplates,
            grants, PlanCatalogDto.Current.Plans, PlanCatalogDto.Current.Packs,
            string.IsNullOrWhiteSpace(options.Value.ContactEmail) ? null : options.Value.ContactEmail.Trim(),
            org.SubscriptionStatus is { } status
                ? new SubscriptionDto(status, org.SubscriptionInterval, org.HasLiveSubscription ? org.SubscriptionCancelAt : null, org.HasLiveSubscription ? org.PlanPeriodEnd : null)
                : null,
            stripe.Value.Enabled);
    }
}

internal sealed class BillingService(
    AppDbContext db, ICurrentTenant tenant, BillingReader reader, IServiceProvider services, ILogger<BillingService> logger) : IBillingService
{
    public async Task<BillingDto> GetAsync(CancellationToken ct)
    {
        var org = await db.Organizations.SingleAsync(o => o.Id == tenant.RequireOrganizationId(), ct);

        // Com assinatura, confere o estado atual no Stripe: quem volta do portal vê a mudança na hora,
        // sem depender de o webhook já ter chegado. Se o Stripe não responder, mostra o que está salvo.
        if (org.HasLiveSubscription && org.StripeSubscriptionId is { } subscriptionId
            && services.GetService<Stripe.StripeSubscriptionSync>() is { } sync)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                org = await sync.SyncAsync(subscriptionId, timeout.Token) ?? org;
            }
            catch (Exception e) when (e is global::Stripe.StripeException or HttpRequestException or InvalidOperationException
                || e is OperationCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(e, "Não foi possível atualizar a assinatura {SubscriptionId} no Stripe", subscriptionId);
            }
        }
        return await reader.BuildAsync(org, ct);
    }
}

/// <summary>Operação manual da equipe da plataforma: trocar plano e conceder créditos, enquanto não há checkout.</summary>
internal sealed class PlatformBillingService(AppDbContext db, UsageMeter meter, BillingReader reader, TimeProvider clock) : IPlatformBillingService
{
    public async Task<IReadOnlyList<PlatformOrganizationItem>> ListAsync(string? query, CancellationToken ct)
    {
        var q = db.Organizations.AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var like = $"%{query.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            q = q.Where(o => EF.Functions.ILike(o.Name, like) || EF.Functions.ILike(o.Slug, like));
        }
        var orgs = await q.OrderByDescending(o => o.CreatedAt).Take(100).ToListAsync(ct);

        var items = new List<PlatformOrganizationItem>(orgs.Count);
        foreach (var org in orgs)
        {
            var s = await meter.SnapshotAsync(org, ct);
            items.Add(new PlatformOrganizationItem(org.Id, org.Name, org.Slug, org.PlanId, org.PlanPeriodEnd,
                s.PlanUsed, s.Plan.ContractsPerPeriod, s.CreditsAvailable, org.CreatedAt));
        }
        return items;
    }

    public async Task<Result<BillingDto>> ChangePlanAsync(string slug, ChangePlanRequest req, CancellationToken ct)
    {
        var org = await db.Organizations.SingleOrDefaultAsync(o => o.Slug == slug, ct);
        if (org is null)
            return Error.NotFound("Organização não encontrada.");

        var errors = new ValidationErrors();
        var plan = Plans.Find(req.Plan);
        if (plan is null)
            errors.Add("plan", $"Plano inválido. Use: {string.Join(", ", Plans.All.Select(p => p.Id))}.");
        if ((req.PeriodStart is null) != (req.PeriodEnd is null))
            errors.Add("periodEnd", "Informe início e fim do ciclo, ou nenhum dos dois (mês do calendário).");
        else if (req.PeriodStart >= req.PeriodEnd)
            errors.Add("periodEnd", "O fim do ciclo precisa ser depois do início.");
        if (!errors.IsEmpty)
            return Error.Validation(errors);
        if (org.HasLiveSubscription)
            return Error.Conflict("Esta organização tem assinatura ativa no Stripe; o plano muda pelo portal de assinatura.", "managed_by_stripe");

        org.ChangePlan(plan!.Id, req.PeriodStart?.ToUniversalTime(), req.PeriodEnd?.ToUniversalTime());
        await db.SaveChangesAsync(ct);
        return await reader.BuildAsync(org, ct);
    }

    public async Task<Result<BillingDto>> GrantCreditsAsync(string slug, GrantCreditsRequest req, CancellationToken ct)
    {
        var org = await db.Organizations.SingleOrDefaultAsync(o => o.Slug == slug, ct);
        if (org is null)
            return Error.NotFound("Organização não encontrada.");

        var errors = new ValidationErrors();
        if (req.Quantity is not (>= 1 and <= 10_000))
            errors.Add("quantity", "Informe de 1 a 10.000 contratos.");
        var months = req.ValidityMonths ?? Plans.CreditValidityMonths;
        if (months is < 1 or > 36)
            errors.Add("validityMonths", "A validade vai de 1 a 36 meses.");
        if ((req.Note?.Length ?? 0) > 300)
            errors.Add("note", "Use no máximo 300 caracteres.");
        if ((req.Reference?.Length ?? 0) > 200)
            errors.Add("reference", "Use no máximo 200 caracteres.");
        if (!errors.IsEmpty)
            return Error.Validation(errors);

        var reference = string.IsNullOrWhiteSpace(req.Reference) ? null : req.Reference.Trim();
        // Mesmo pedido repetido (mesma referência): não concede de novo.
        if (reference is null || !await db.CreditGrants.AnyAsync(g => g.OrganizationId == org.Id && g.Reference == reference, ct))
        {
            var now = clock.GetUtcNow();
            db.CreditGrants.Add(new CreditGrant
            {
                OrganizationId = org.Id,
                Quantity = req.Quantity!.Value,
                Remaining = req.Quantity.Value,
                CreatedAt = now,
                ExpiresAt = now.AddMonths(months),
                Source = CreditSources.Manual,
                Reference = reference,
                Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (reference is not null)
            {
                db.ChangeTracker.Clear(); // a mesma referência entrou por outra requisição ao mesmo tempo
                org = await db.Organizations.SingleAsync(o => o.Slug == slug, ct);
            }
        }
        return await reader.BuildAsync(org, ct);
    }
}

using Contratos.Application.Billing;
using Contratos.Application.Common;
using Contratos.Application.Platform;
using Contratos.Domain.Billing;
using Contratos.Domain.Contracts;
using Contratos.Domain.Organizations;
using Contratos.Domain.Users;
using Contratos.Infrastructure.Billing;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Infrastructure.Platform;

/// <summary>
/// Portal super admin. Enxerga todas as organizações: toda consulta a contratos e templates usa
/// <c>IgnoreQueryFilters()</c> com o filtro de organização explícito (a requisição tem o tenant de quem
/// está logado, que não é o da conta consultada). Só é chamado por rotas com a política PlatformAdmin.
/// </summary>
internal sealed class SuperAdminService(
    AppDbContext db, UsageMeter meter, BillingReader reader, IPlatformBillingService billing,
    PlatformAuditWriter audit, ICurrentTenant tenant, TimeProvider clock) : ISuperAdminService
{
    private static readonly TimeSpan Brt = TimeSpan.FromHours(-3);
    private const string LegacyFreeId = "gratis";
    private static readonly string[] LiveStatuses = ["active", "trialing", "past_due"];

    // ---------- Lista ----------

    public async Task<Paged<TenantListItem>> ListAsync(TenantListQuery query, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);
        var q = db.Organizations.IgnoreQueryFilters().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var like = $"%{query.Q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            q = q.Where(o => EF.Functions.ILike(o.Name, like) || EF.Functions.ILike(o.Slug, like)
                || db.Users.Any(u => u.OrganizationId == o.Id && EF.Functions.ILike(u.Email!, like)));
        }
        if (Plans.Find(query.Plan) is { } plan)
            q = plan.Id == Plans.PayAsYouGoId
                ? q.Where(o => o.PlanId == Plans.PayAsYouGoId || o.PlanId == LegacyFreeId)
                : q.Where(o => o.PlanId == plan.Id);
        q = FilterStatus(q, query.Status);
        q = query.Activity switch
        {
            "active7" => q.Where(o => db.Users.Any(u => u.OrganizationId == o.Id && u.LastSeenAt >= now.AddDays(-7))),
            "inactive30" => q.Where(o => !db.Users.Any(u => u.OrganizationId == o.Id && u.LastSeenAt >= now.AddDays(-30))),
            "never" => q.Where(o => !db.Users.Any(u => u.OrganizationId == o.Id && u.LastSeenAt != null)),
            _ => q,
        };

        var total = await q.CountAsync(ct);
        IQueryable<Organization> ordered = query.Sort switch
        {
            "created" => q.OrderByDescending(o => o.CreatedAt),
            "oldest" => q.OrderBy(o => o.CreatedAt),
            "name" => q.OrderBy(o => o.Name),
            "contracts" => q.OrderByDescending(o => db.Contracts.Count(c => c.OrganizationId == o.Id && c.CreatedAt >= now.AddDays(-30)))
                .ThenByDescending(o => o.CreatedAt),
            // Padrão: último acesso, quem nunca entrou por último.
            _ => q.OrderByDescending(o => db.Users.Any(u => u.OrganizationId == o.Id && u.LastSeenAt != null))
                .ThenByDescending(o => db.Users.Where(u => u.OrganizationId == o.Id).Max(u => u.LastSeenAt))
                .ThenByDescending(o => o.CreatedAt),
        };
        var orgs = await ordered.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new Paged<TenantListItem>(await BuildItemsAsync(orgs, ct), page, size, total);
    }

    private static IQueryable<Organization> FilterStatus(IQueryable<Organization> q, string? status) => status switch
    {
        TenantStatuses.Suspended => q.Where(o => o.SuspendedAt != null),
        TenantStatuses.PastDue or TenantStatuses.Trialing or TenantStatuses.Active =>
            q.Where(o => o.SuspendedAt == null && o.SubscriptionStatus == status),
        TenantStatuses.Manual => q.Where(o => o.SuspendedAt == null
            && (o.SubscriptionStatus == null || !LiveStatuses.Contains(o.SubscriptionStatus))
            && o.PlanId != Plans.PayAsYouGoId && o.PlanId != LegacyFreeId),
        TenantStatuses.Canceled => q.Where(o => o.SuspendedAt == null && o.SubscriptionStatus != null
            && !LiveStatuses.Contains(o.SubscriptionStatus) && (o.PlanId == Plans.PayAsYouGoId || o.PlanId == LegacyFreeId)),
        TenantStatuses.PayAsYouGo => q.Where(o => o.SuspendedAt == null && o.SubscriptionStatus == null
            && (o.PlanId == Plans.PayAsYouGoId || o.PlanId == LegacyFreeId)),
        _ => q,
    };

    /// <summary>Mesma regra de <see cref="FilterStatus"/>, para uma organização já carregada.</summary>
    private static string StatusOf(Organization o) =>
        o.IsSuspended ? TenantStatuses.Suspended
        : o.HasLiveSubscription ? o.SubscriptionStatus!
        : o.Plan.Id != Plans.PayAsYouGoId ? TenantStatuses.Manual
        : o.SubscriptionStatus is not null ? TenantStatuses.Canceled
        : TenantStatuses.PayAsYouGo;

    /// <summary>Cartões da lista: contagens em poucas consultas agrupadas, mais a cota de cada conta.</summary>
    private async Task<IReadOnlyList<TenantListItem>> BuildItemsAsync(IReadOnlyList<Organization> orgs, CancellationToken ct)
    {
        if (orgs.Count == 0)
            return [];
        var ids = orgs.Select(o => o.Id).ToList();
        var since30 = clock.GetUtcNow().AddDays(-30);

        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.OrganizationId))
            .Select(u => new { u.OrganizationId, u.Name, u.Email, u.Role, u.DeactivatedAt, u.LastSeenAt, u.CreatedAt })
            .ToListAsync(ct);
        var templates = await db.Templates.IgnoreQueryFilters().Where(t => ids.Contains(t.OrganizationId) && t.ArchivedAt == null)
            .GroupBy(t => t.OrganizationId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var contracts = await db.Contracts.IgnoreQueryFilters().Where(c => ids.Contains(c.OrganizationId))
            .GroupBy(c => new { c.OrganizationId, c.Status })
            .Select(g => new { g.Key.OrganizationId, g.Key.Status, Count = g.Count(), Recent = g.Count(c => c.CreatedAt >= since30) })
            .ToListAsync(ct);
        var keys = await db.ApiKeys.Where(k => ids.Contains(k.OrganizationId) && k.RevokedAt == null)
            .GroupBy(k => k.OrganizationId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var hooks = await db.WebhookEndpoints.Where(w => ids.Contains(w.OrganizationId))
            .GroupBy(w => w.OrganizationId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var origins = await db.EmbedOrigins.Where(e => ids.Contains(e.OrganizationId))
            .GroupBy(e => e.OrganizationId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var items = new List<TenantListItem>(orgs.Count);
        foreach (var org in orgs)
        {
            var snapshot = await meter.SnapshotAsync(org, ct);
            var mine = users.Where(u => u.OrganizationId == org.Id).ToList();
            var owner = mine.Where(u => u.Role == UserRole.Admin && u.DeactivatedAt == null).MinBy(u => u.CreatedAt)
                ?? mine.MinBy(u => u.CreatedAt);
            var byStatus = contracts.Where(c => c.OrganizationId == org.Id).ToDictionary(c => c.Status);
            int Count(params ContractStatus[] s) => s.Sum(x => byStatus.TryGetValue(x, out var v) ? v.Count : 0);
            var plan = org.Plan;

            items.Add(new TenantListItem(
                org.Id, org.Name, org.Slug, org.CreatedAt, plan.Id, plan.Name, StatusOf(org), org.SubscriptionInterval,
                org.HasLiveSubscription ? org.SubscriptionCancelAt : null, org.SuspendedAt,
                new TenantUsage(snapshot.PlanUsed, plan.ContractsPerPeriod, snapshot.CreditsAvailable,
                    mine.Count(u => u.DeactivatedAt == null), plan.MaxUsers,
                    templates.GetValueOrDefault(org.Id), plan.MaxTemplates, snapshot.PeriodStart, snapshot.PeriodEnd),
                new ContractCounts(
                    byStatus.Values.Sum(v => v.Count), Count(ContractStatus.Draft),
                    Count(ContractStatus.Sent, ContractStatus.PartiallySigned), Count(ContractStatus.Completed),
                    Count(ContractStatus.Declined), Count(ContractStatus.Expired), Count(ContractStatus.Cancelled),
                    byStatus.Values.Sum(v => v.Recent)),
                new TenantIntegrations(keys.GetValueOrDefault(org.Id), hooks.GetValueOrDefault(org.Id), origins.GetValueOrDefault(org.Id)),
                mine.Max(u => u.LastSeenAt),
                owner is null ? null : new TenantOwner(owner.Name, owner.Email!)));
        }
        return items;
    }

    // ---------- Visão geral ----------

    public async Task<PlatformOverviewDto> OverviewAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var orgs = await db.Organizations.AsNoTracking().ToListAsync(ct);
        var lastSeen = await db.Users.Where(u => u.LastSeenAt != null)
            .GroupBy(u => u.OrganizationId).Select(g => new { g.Key, Last = g.Max(u => u.LastSeenAt) })
            .ToDictionaryAsync(x => x.Key, x => x.Last, ct);

        var local = now.ToOffset(Brt);
        // O Npgsql só grava timestamptz em UTC.
        var monthStartBrt = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, Brt);
        var monthStart = monthStartBrt.ToUniversalTime();
        var lastMonthStart = monthStartBrt.AddMonths(-1).ToUniversalTime();
        var since30 = now.AddDays(-30);
        var usage = await db.UsageRecords.Where(u => u.RefundedAt == null && u.ConsumedAt >= lastMonthStart)
            .Select(u => u.ConsumedAt).ToListAsync(ct);
        var completed = await db.Contracts.IgnoreQueryFilters().CountAsync(c => c.CompletedAt >= monthStart, ct);

        decimal mrr = 0;
        var paid = 0;
        foreach (var o in orgs.Where(o => o.SubscriptionStatus is "active" or "past_due"))
        {
            paid++;
            mrr += o.SubscriptionInterval == "year" ? Math.Round(o.Plan.AnnualPrice / 12, 2) : o.Plan.MonthlyPrice;
        }

        var attentionOrgs = orgs
            .Where(o => o.IsSuspended || o.IsPaymentOverdue || (o.HasLiveSubscription && o.SubscriptionCancelAt is not null))
            .OrderByDescending(o => o.IsPaymentOverdue).ThenByDescending(o => o.CreatedAt).Take(8).ToList();
        var newestOrgs = orgs.OrderByDescending(o => o.CreatedAt).Take(6).ToList();

        return new PlatformOverviewDto(
            orgs.Count,
            orgs.Count(o => o.CreatedAt >= now.AddDays(-7)),
            orgs.Count(o => o.CreatedAt >= since30),
            orgs.Count(o => lastSeen.GetValueOrDefault(o.Id) >= now.AddDays(-7)),
            orgs.Count(o => !(lastSeen.GetValueOrDefault(o.Id) >= since30)),
            mrr, paid,
            usage.Count(t => t >= monthStart), usage.Count(t => t < monthStart), completed,
            orgs.GroupBy(o => o.Plan.Id).Select(g => new KeyCount(g.Key, g.Count()))
                .OrderBy(k => Plans.All.ToList().FindIndex(p => p.Id == k.Key)).ToList(),
            orgs.GroupBy(StatusOf).Select(g => new KeyCount(g.Key, g.Count()))
                .OrderBy(k => TenantStatuses.All.ToList().IndexOf(k.Key)).ToList(),
            Daily(orgs.Select(o => o.CreatedAt), now, 30),
            Daily(usage, now, 30),
            await BuildItemsAsync(attentionOrgs, ct),
            await BuildItemsAsync(newestOrgs, ct));
    }

    private static DateOnly DayOf(DateTimeOffset t) => DateOnly.FromDateTime(t.ToOffset(Brt).DateTime);

    /// <summary>Contagem por dia (Brasília) nos últimos <paramref name="days"/> dias, com os dias zerados.</summary>
    private static List<DailyCount> Daily(IEnumerable<DateTimeOffset> times, DateTimeOffset now, int days)
    {
        var today = DayOf(now);
        var counts = times.Select(DayOf).Where(d => d > today.AddDays(-days)).GroupBy(d => d).ToDictionary(g => g.Key, g => g.Count());
        return Enumerable.Range(0, days).Select(i => today.AddDays(i - days + 1))
            .Select(d => new DailyCount(d, counts.GetValueOrDefault(d))).ToList();
    }

    // ---------- Detalhe ----------

    public async Task<Result<TenantDetailDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var org = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);
        if (org is null)
            return Error.NotFound("Organização não encontrada.");

        var summary = (await BuildItemsAsync([org], ct))[0];
        var users = await db.Users.AsNoTracking().Where(u => u.OrganizationId == id)
            .OrderBy(u => u.DeactivatedAt != null).ThenBy(u => u.CreatedAt)
            .ToListAsync(ct);

        var perTemplate = await db.Contracts.IgnoreQueryFilters().Where(c => c.OrganizationId == id)
            .Join(db.TemplateVersions, c => c.TemplateVersionId, v => v.Id, (c, v) => v.TemplateId)
            .GroupBy(t => t).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var templates = (await db.Templates.IgnoreQueryFilters().AsNoTracking().Where(t => t.OrganizationId == id)
                .Select(t => new { t.Id, t.Name, t.ArchivedAt, t.UpdatedAt }).ToListAsync(ct))
            .Select(t => new TemplateUsageDto(t.Id, t.Name, t.ArchivedAt is not null, perTemplate.GetValueOrDefault(t.Id), t.UpdatedAt))
            .OrderBy(t => t.Archived).ThenByDescending(t => t.Contracts).ThenByDescending(t => t.UpdatedAt)
            .Take(15).ToList();

        var auditEntries = await db.PlatformAudit.AsNoTracking().Where(a => a.OrganizationId == id)
            .OrderByDescending(a => a.CreatedAt).Take(20)
            .Select(a => new AuditEntryDto(a.Action, a.ActorName, a.Details, a.CreatedAt))
            .ToListAsync(ct);

        return new TenantDetailDto(
            summary, org.SuspensionReason, org.StripeCustomerId,
            await reader.BuildAsync(org, ct),
            users.Select(u => new TenantUserDto(u.Id, u.Name, u.Email!, u.Role.ToString(),
                u.DeactivatedAt is not null ? "Desativado" : u.IsPendingInvite ? "Convidado" : !u.EmailConfirmed ? "Não confirmado" : "Ativo",
                u.EmailConfirmed, u.CreatedAt, u.LastSeenAt)).ToList(),
            templates, auditEntries);
    }

    // ---------- Linha do tempo ----------

    private sealed record RawEvent(string Category, string Type, DateTimeOffset At, Guid? ActorId = null, string? ActorName = null, string? Sample = null);

    private const int MaxPerSource = 3000;

    /// <summary>Contratos: o que entra na linha do tempo e em qual categoria (o resto é detalhe interno).</summary>
    private static readonly Dictionary<string, string> ContractEventCategories = new()
    {
        [ContractEventTypes.Created] = TimelineCategories.Contracts,
        [ContractEventTypes.Sent] = TimelineCategories.Contracts,
        [ContractEventTypes.SendFailed] = TimelineCategories.Contracts,
        [ContractEventTypes.Completed] = TimelineCategories.Contracts,
        [ContractEventTypes.Cancelled] = TimelineCategories.Contracts,
        [ContractEventTypes.Expired] = TimelineCategories.Contracts,
        [ContractEventTypes.SignerViewed] = TimelineCategories.Signatures,
        [ContractEventTypes.SignerSigned] = TimelineCategories.Signatures,
        [ContractEventTypes.SignerDeclined] = TimelineCategories.Signatures,
        [ContractEventTypes.DeliveryFailed] = TimelineCategories.Signatures,
        [ContractEventTypes.LinkResent] = TimelineCategories.Signatures,
    };

    public async Task<Result<IReadOnlyList<TimelineGroupDto>>> TimelineAsync(Guid id, string? category, int days, CancellationToken ct)
    {
        if (!await db.Organizations.AnyAsync(o => o.Id == id, ct))
            return Error.NotFound("Organização não encontrada.");
        if (category is not null && !TimelineCategories.All.Contains(category))
            return Error.Validation("category", $"Categoria inválida. Use: {string.Join(", ", TimelineCategories.All)}.");

        days = Math.Clamp(days, 1, 365);
        var since = clock.GetUtcNow().AddDays(-days);
        bool Wants(string c) => category is null || category == c;
        var events = new List<RawEvent>();

        if (Wants(TimelineCategories.Account))
        {
            var org = await db.Organizations.Where(o => o.Id == id).Select(o => new { o.CreatedAt, o.SuspendedAt }).SingleAsync(ct);
            if (org.CreatedAt >= since)
                events.Add(new(TimelineCategories.Account, "OrganizationCreated", org.CreatedAt));
            var users = await db.Users.AsNoTracking().Where(u => u.OrganizationId == id)
                .Select(u => new { u.Id, u.Name, u.CreatedAt, u.InvitedAt, u.InvitedById, u.DeactivatedAt, u.LastSeenAt }).ToListAsync(ct);
            foreach (var u in users)
            {
                if (u.InvitedAt is { } invited && invited >= since)
                    events.Add(new(TimelineCategories.Account, "UserInvited", invited, u.InvitedById, Sample: u.Name));
                else if (u.InvitedAt is null && u.CreatedAt >= since)
                    events.Add(new(TimelineCategories.Account, "UserCreated", u.CreatedAt, Sample: u.Name));
                if (u.DeactivatedAt is { } off && off >= since)
                    events.Add(new(TimelineCategories.Account, "UserDeactivated", off, Sample: u.Name));
                if (u.LastSeenAt is { } seen && seen >= since)
                    events.Add(new(TimelineCategories.Account, "UserLastSeen", seen, u.Id));
            }
        }

        if (Wants(TimelineCategories.Contracts) || Wants(TimelineCategories.Signatures))
        {
            var types = ContractEventCategories.Where(kv => Wants(kv.Value)).Select(kv => kv.Key).ToList();
            var rows = await db.ContractEvents
                .Join(db.Contracts.IgnoreQueryFilters().Where(c => c.OrganizationId == id), e => e.ContractId, c => c.Id,
                    (e, c) => new { e.Type, e.OccurredAt, e.ActorUserId, c.Number })
                .Where(x => x.OccurredAt >= since && types.Contains(x.Type))
                .OrderByDescending(x => x.OccurredAt).Take(MaxPerSource)
                .ToListAsync(ct);
            events.AddRange(rows.Select(r => new RawEvent(ContractEventCategories[r.Type], r.Type, r.OccurredAt, r.ActorUserId,
                Sample: $"#{r.Number:0000}")));
        }

        if (Wants(TimelineCategories.Templates))
        {
            var templates = await db.Templates.IgnoreQueryFilters().AsNoTracking().Where(t => t.OrganizationId == id)
                .Select(t => new { t.Id, t.Name, t.CreatedAt, t.CreatedBy, t.ArchivedAt }).ToListAsync(ct);
            var names = templates.ToDictionary(t => t.Id, t => t.Name);
            foreach (var t in templates)
            {
                if (t.CreatedAt >= since)
                    events.Add(new(TimelineCategories.Templates, "TemplateCreated", t.CreatedAt, t.CreatedBy, Sample: t.Name));
                if (t.ArchivedAt is { } archived && archived >= since)
                    events.Add(new(TimelineCategories.Templates, "TemplateArchived", archived, Sample: t.Name));
            }
            var ids = names.Keys.ToList();
            var published = await db.TemplateVersions.Where(v => ids.Contains(v.TemplateId) && v.PublishedAt >= since)
                .OrderByDescending(v => v.PublishedAt).Take(MaxPerSource)
                .Select(v => new { v.TemplateId, v.PublishedAt, v.CreatedBy, v.CreatedByExternal }).ToListAsync(ct);
            events.AddRange(published.Select(v => new RawEvent(TimelineCategories.Templates, "TemplatePublished", v.PublishedAt!.Value,
                v.CreatedByExternal is null ? v.CreatedBy : null, v.CreatedByExternal, names[v.TemplateId])));
        }

        if (Wants(TimelineCategories.Billing))
        {
            var grants = await db.CreditGrants.Where(g => g.OrganizationId == id && g.CreatedAt >= since)
                .Select(g => new { g.Source, g.Quantity, g.CreatedAt }).ToListAsync(ct);
            events.AddRange(grants.Select(g => new RawEvent(TimelineCategories.Billing, g.Source switch
            {
                CreditSources.Stripe => "CreditsPurchased",
                CreditSources.Welcome => "CreditsWelcome",
                _ => "CreditsManual",
            }, g.CreatedAt, Sample: $"+{g.Quantity}")));
        }

        if (Wants(TimelineCategories.Integrations))
        {
            var keys = await db.ApiKeys.Where(k => k.OrganizationId == id && (k.CreatedAt >= since || k.RevokedAt >= since))
                .Select(k => new { k.Name, k.CreatedAt, k.CreatedBy, k.RevokedAt }).ToListAsync(ct);
            foreach (var k in keys)
            {
                if (k.CreatedAt >= since)
                    events.Add(new(TimelineCategories.Integrations, "ApiKeyCreated", k.CreatedAt, k.CreatedBy, Sample: k.Name));
                if (k.RevokedAt is { } revoked && revoked >= since)
                    events.Add(new(TimelineCategories.Integrations, "ApiKeyRevoked", revoked, Sample: k.Name));
            }
            var hooks = await db.WebhookEndpoints.Where(w => w.OrganizationId == id && w.CreatedAt >= since)
                .Select(w => new { w.Url, w.CreatedAt, w.CreatedBy }).ToListAsync(ct);
            events.AddRange(hooks.Select(w => new RawEvent(TimelineCategories.Integrations, "WebhookCreated", w.CreatedAt, w.CreatedBy,
                Sample: Uri.TryCreate(w.Url, UriKind.Absolute, out var u) ? u.Host : null)));
            var origins = await db.EmbedOrigins.Where(e => e.OrganizationId == id && e.CreatedAt >= since)
                .Select(e => new { e.Origin, e.CreatedAt, e.CreatedBy }).ToListAsync(ct);
            events.AddRange(origins.Select(e => new RawEvent(TimelineCategories.Integrations, "EmbedOriginAdded", e.CreatedAt, e.CreatedBy, Sample: e.Origin)));
        }

        if (Wants(TimelineCategories.Platform))
        {
            var entries = await db.PlatformAudit.Where(a => a.OrganizationId == id && a.CreatedAt >= since)
                .OrderByDescending(a => a.CreatedAt).Take(MaxPerSource)
                .Select(a => new { a.Action, a.CreatedAt, a.ActorName, a.Details }).ToListAsync(ct);
            events.AddRange(entries.Select(a => new RawEvent(TimelineCategories.Platform, a.Action, a.CreatedAt, ActorName: a.ActorName, Sample: a.Details)));
        }

        // Nomes de quem fez: usuários desta organização (ou de qualquer uma, para ações da equipe pelo portal).
        var actorIds = events.Where(e => e.ActorId is not null && e.ActorName is null).Select(e => e.ActorId!.Value).Distinct().ToList();
        var actorNames = await db.Users.Where(u => actorIds.Contains(u.Id)).Select(u => new { u.Id, u.Name })
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        IReadOnlyList<TimelineGroupDto> groups = events
            .GroupBy(e => (Day: DayOf(e.At), e.Category, e.Type))
            .Select(g => new TimelineGroupDto(g.Key.Day, g.Key.Category, g.Key.Type, g.Count(), g.Max(e => e.At),
                g.OrderByDescending(e => e.At)
                    .Select(e => e.ActorName ?? (e.ActorId is { } a ? actorNames.GetValueOrDefault(a) : null))
                    .OfType<string>().Distinct().Take(3).ToList(),
                g.OrderByDescending(e => e.At).Select(e => e.Sample).OfType<string>().Distinct().Take(3).ToList()))
            .OrderByDescending(g => g.Day).ThenByDescending(g => g.LastAt)
            .ToList();
        return Result<IReadOnlyList<TimelineGroupDto>>.Ok(groups);
    }

    // ---------- Ações ----------

    public async Task<Result<TenantDetailDto>> ChangePlanAsync(Guid id, ChangePlanRequest request, CancellationToken ct)
    {
        if (await SlugAsync(id, ct) is not { } slug)
            return Error.NotFound("Organização não encontrada.");
        var result = await billing.ChangePlanAsync(slug, request, ct);
        return result.IsSuccess ? await GetAsync(id, ct) : result.Error!;
    }

    public async Task<Result<TenantDetailDto>> GrantCreditsAsync(Guid id, GrantCreditsRequest request, CancellationToken ct)
    {
        if (await SlugAsync(id, ct) is not { } slug)
            return Error.NotFound("Organização não encontrada.");
        var result = await billing.GrantCreditsAsync(slug, request, ct);
        return result.IsSuccess ? await GetAsync(id, ct) : result.Error!;
    }

    public async Task<Result<TenantDetailDto>> SuspendAsync(Guid id, SuspendRequest request, CancellationToken ct)
    {
        var org = await db.Organizations.SingleOrDefaultAsync(o => o.Id == id, ct);
        if (org is null)
            return Error.NotFound("Organização não encontrada.");
        if (id == tenant.OrganizationId)
            return Error.Conflict("Você não pode suspender a organização da sua própria conta.", "own_organization");
        if ((request.Reason?.Trim().Length ?? 0) > Organization.SuspensionReasonMaxLength)
            return Error.Validation("reason", $"Use no máximo {Organization.SuspensionReasonMaxLength} caracteres.");
        if (org.IsSuspended)
            return await GetAsync(id, ct);

        org.Suspend(request.Reason, clock.GetUtcNow());
        await audit.AddAsync(id, PlatformActions.Suspended, org.SuspensionReason, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        // Sessões abertas caem na próxima validação do cookie (até 1 minuto).
        var stamp = Guid.NewGuid().ToString();
        await db.Users.Where(u => u.OrganizationId == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.SecurityStamp, stamp), ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result<TenantDetailDto>> ReactivateAsync(Guid id, CancellationToken ct)
    {
        var org = await db.Organizations.SingleOrDefaultAsync(o => o.Id == id, ct);
        if (org is null)
            return Error.NotFound("Organização não encontrada.");
        if (!org.IsSuspended)
            return await GetAsync(id, ct);

        org.Reactivate();
        await audit.AddAsync(id, PlatformActions.Reactivated, null, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private Task<string?> SlugAsync(Guid id, CancellationToken ct) =>
        db.Organizations.Where(o => o.Id == id).Select(o => o.Slug).SingleOrDefaultAsync(ct);
}

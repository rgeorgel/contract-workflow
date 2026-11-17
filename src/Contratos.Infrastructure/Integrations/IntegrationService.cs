using Contratos.Application.Common;
using Contratos.Application.Integrations;
using Contratos.Domain.Billing;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Integrations;

internal sealed class IntegrationService(
    AppDbContext db,
    ICurrentTenant tenant,
    WebhookSecrets secrets,
    TimeProvider clock,
    IOptions<WebhookOptions> options,
    Billing.UsageMeter meter,
    ILogger<IntegrationService> logger) : IIntegrationService
{
    public const int MaxKeys = 10;
    public const int MaxEndpoints = 10;
    private const int DeliveriesPageSize = 25;

    private static readonly Error AdminOnly = Error.Forbidden("Só um Admin pode gerenciar as integrações.");
    private static readonly Error KeyNotFound = Error.NotFound("Chave não encontrada.");
    private static readonly Error EndpointNotFound = Error.NotFound("Webhook não encontrado.");

    private static Error NotInPlan() => IntegrationErrors.NotInPlan();
    private static IReadOnlyList<string> PlansWithApi() => IntegrationErrors.PlansWithApi();

    public async Task<Result<IntegrationsOverviewDto>> GetAsync(CancellationToken ct)
    {
        if (!tenant.IsAdmin())
            return AdminOnly;
        var orgId = tenant.RequireOrganizationId();
        var org = await db.Organizations.SingleAsync(o => o.Id == orgId, ct);
        return new IntegrationsOverviewDto(
            org.Plan.ApiAccess, PlansWithApi(), await KeysAsync(orgId, ct), await EndpointsAsync(orgId, ct),
            WebhookEventTypes.All, MaxKeys, MaxEndpoints, await OriginsAsync(orgId, ct), EmbedOrigins.MaxPerOrganization);
    }

    public async Task<PublicAccount> GetAccountAsync(CancellationToken ct)
    {
        var org = await db.Organizations.SingleAsync(o => o.Id == tenant.RequireOrganizationId(), ct);
        var s = await meter.SnapshotAsync(org, ct);
        return new PublicAccount(
            new PublicOrganization(org.Name, org.Slug, s.Plan.Id, s.Plan.Name),
            new PublicPlanUsage(s.PeriodStart, s.PeriodEnd, s.PlanUsed, s.Plan.ContractsPerPeriod, s.CreditsAvailable,
                s.AvailableSends, org.IsPaymentOverdue));
    }

    // ---------- Chaves ----------

    public async Task<Result<CreatedApiKeyDto>> CreateKeyAsync(CreateApiKeyRequest request, CancellationToken ct)
    {
        if (await GuardAsync(ct) is { } denied)
            return denied;
        var orgId = tenant.RequireOrganizationId();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return Error.Validation("name", "Dê um nome para reconhecer a chave, ex.: “CRM” ou “ERP”.");
        if (name.Length > ApiKey.NameMaxLength)
            return Error.Validation("name", $"Use no máximo {ApiKey.NameMaxLength} caracteres.");
        if (await db.ApiKeys.CountAsync(k => k.OrganizationId == orgId && k.RevokedAt == null, ct) >= MaxKeys)
            return Error.Conflict($"Sua conta já tem {MaxKeys} chaves ativas. Revogue uma que não usa mais.", "too_many_keys");

        var (token, display, hash) = ApiKeyTokens.Create();
        var key = new ApiKey
        {
            OrganizationId = orgId, Name = name, Prefix = display, Hash = hash,
            CreatedBy = tenant.RequireUserId(), CreatedAt = clock.GetUtcNow(),
        };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Chave de API {KeyId} criada na organização {OrganizationId}", key.Id, orgId);
        return new CreatedApiKeyDto((await KeysAsync(orgId, ct)).Single(k => k.Id == key.Id), token);
    }

    public async Task<Result> RevokeKeyAsync(Guid id, CancellationToken ct)
    {
        if (!tenant.IsAdmin())
            return AdminOnly;
        var orgId = tenant.RequireOrganizationId();
        var key = await db.ApiKeys.SingleOrDefaultAsync(k => k.Id == id && k.OrganizationId == orgId, ct);
        if (key is null)
            return KeyNotFound;
        key.RevokedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Chave de API {KeyId} revogada", key.Id);
        return Result.Ok();
    }

    private async Task<IReadOnlyList<ApiKeyDto>> KeysAsync(Guid orgId, CancellationToken ct) =>
        await db.ApiKeys.Where(k => k.OrganizationId == orgId)
            .OrderBy(k => k.RevokedAt != null).ThenByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyDto(k.Id, k.Name, k.Prefix,
                db.Users.Where(u => u.Id == k.CreatedBy).Select(u => u.Name).FirstOrDefault(),
                k.CreatedAt, k.LastUsedAt, k.RevokedAt))
            .ToListAsync(ct);

    // ---------- Webhooks ----------

    public async Task<Result<IReadOnlyList<WebhookEndpointDto>>> ListEndpointsAsync(CancellationToken ct)
    {
        if (!CanManageHooks)
            return AdminOnly;
        return Result<IReadOnlyList<WebhookEndpointDto>>.Ok(await EndpointsAsync(tenant.RequireOrganizationId(), ct));
    }

    public async Task<Result<CreatedWebhookEndpointDto>> CreateEndpointAsync(WebhookEndpointRequest request, CancellationToken ct)
    {
        if (await GuardAsync(ct, allowApiKey: true) is { } denied)
            return denied;
        var orgId = tenant.RequireOrganizationId();
        var errors = Validate(request);
        if (!errors.IsEmpty)
            return Error.Validation(errors);
        if (await db.WebhookEndpoints.CountAsync(e => e.OrganizationId == orgId, ct) >= MaxEndpoints)
            return Error.Conflict($"Sua conta já tem {MaxEndpoints} webhooks. Exclua um que não usa mais.", "too_many_endpoints");

        var secret = WebhookSecrets.NewSecret();
        var endpoint = new WebhookEndpoint
        {
            OrganizationId = orgId,
            Url = request.Url!.Trim(),
            Description = Clean(request.Description),
            Events = NormalizeEvents(request.Events),
            Enabled = request.Enabled ?? true,
            SecretProtected = secrets.Protect(secret),
            CreatedBy = tenant.RequireUserId(),
            CreatedByApiKeyId = tenant.ApiKeyId,
            CreatedAt = clock.GetUtcNow(),
            UpdatedAt = clock.GetUtcNow(),
        };
        db.WebhookEndpoints.Add(endpoint);
        await db.SaveChangesAsync(ct);
        if (tenant.ApiKeyId is { } keyId)
            logger.LogInformation("Webhook {EndpointId} cadastrado pela API (chave {KeyId}) na organização {OrganizationId}", endpoint.Id, keyId, orgId);
        return new CreatedWebhookEndpointDto((await EndpointsAsync(orgId, ct)).Single(e => e.Id == endpoint.Id), secret);
    }

    public async Task<Result<WebhookEndpointDto>> UpdateEndpointAsync(Guid id, WebhookEndpointRequest request, CancellationToken ct)
    {
        if (!tenant.IsAdmin())
            return AdminOnly;
        var endpoint = await FindEndpointAsync(id, ct);
        if (endpoint is null)
            return EndpointNotFound;
        // Reativar num plano sem API não faria nada: os eventos não são gerados.
        if (request.Enabled == true && !endpoint.Enabled && await GuardAsync(ct) is { } denied)
            return denied;
        var errors = Validate(request);
        if (!errors.IsEmpty)
            return Error.Validation(errors);

        endpoint.Url = request.Url!.Trim();
        endpoint.Description = Clean(request.Description);
        endpoint.Events = NormalizeEvents(request.Events);
        if (request.Enabled is { } enabled)
        {
            if (enabled && !endpoint.Enabled)
                endpoint.FailureStreak = 0;
            endpoint.Enabled = enabled;
        }
        endpoint.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return (await EndpointsAsync(endpoint.OrganizationId, ct)).Single(e => e.Id == endpoint.Id);
    }

    public async Task<Result> DeleteEndpointAsync(Guid id, CancellationToken ct)
    {
        if (!CanManageHooks)
            return AdminOnly;
        var endpoint = await FindEndpointAsync(id, ct);
        if (endpoint is null)
            return EndpointNotFound;
        db.WebhookEndpoints.Remove(endpoint); // entregas vão junto
        await db.SaveChangesAsync(ct);
        if (tenant.ApiKeyId is { } keyId)
            logger.LogInformation("Webhook {EndpointId} excluído pela API (chave {KeyId})", endpoint.Id, keyId);
        return Result.Ok();
    }

    public async Task<Result<WebhookSecretDto>> GetSecretAsync(Guid id, CancellationToken ct)
    {
        if (!tenant.IsAdmin())
            return AdminOnly;
        var endpoint = await FindEndpointAsync(id, ct);
        if (endpoint is null)
            return EndpointNotFound;
        try
        {
            return new WebhookSecretDto(secrets.Unprotect(endpoint.SecretProtected));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return Error.Conflict("Não foi possível ler este segredo. Gere um novo.", "secret_unreadable");
        }
    }

    public async Task<Result<WebhookSecretDto>> RotateSecretAsync(Guid id, CancellationToken ct)
    {
        if (!tenant.IsAdmin())
            return AdminOnly;
        var endpoint = await FindEndpointAsync(id, ct);
        if (endpoint is null)
            return EndpointNotFound;
        var secret = WebhookSecrets.NewSecret();
        endpoint.SecretProtected = secrets.Protect(secret);
        endpoint.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return new WebhookSecretDto(secret);
    }

    public async Task<Result> SendTestAsync(Guid id, CancellationToken ct)
    {
        if (await GuardAsync(ct, allowApiKey: true) is { } denied)
            return denied;
        var endpoint = await FindEndpointAsync(id, ct);
        if (endpoint is null)
            return EndpointNotFound;
        if (!endpoint.Enabled)
            return Error.Conflict("Ative o webhook antes de mandar o teste.", "endpoint_disabled");

        var now = clock.GetUtcNow();
        var ev = new WebhookEvent { OrganizationId = endpoint.OrganizationId, Type = WebhookEventTypes.Test, CreatedAt = now };
        ev.Payload = PublicJson.Serialize(new WebhookPayload(ev.Id, ev.Type, now,
            new WebhookData(null, null, null, tenant.ApiKeyName is { } keyName
                ? $"Evento de teste enviado pela API (chave “{keyName}”). Se chegou com a assinatura certa, está tudo pronto."
                : "Evento de teste enviado pela tela de Integrações. Se chegou com a assinatura certa, está tudo pronto.")));
        db.WebhookEvents.Add(ev);
        db.WebhookDeliveries.Add(new WebhookDelivery { OrganizationId = endpoint.OrganizationId, EventId = ev.Id, EndpointId = endpoint.Id, NextAttemptAt = now });
        await db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    // ---------- Entregas ----------

    public async Task<Result<Paged<WebhookDeliveryDto>>> ListDeliveriesAsync(Guid? endpointId, int page, CancellationToken ct)
    {
        if (!tenant.IsAdmin())
            return AdminOnly;
        var orgId = tenant.RequireOrganizationId();
        page = Math.Max(1, page);
        var query = db.WebhookDeliveries.Where(d => d.OrganizationId == orgId);
        if (endpointId is { } eid)
            query = query.Where(d => d.EndpointId == eid);

        var total = await query.CountAsync(ct);
        // Contratos: o filtro de organização vale (a requisição tem tenant), e o evento é sempre da mesma organização.
        var rows = await query.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            .Skip((page - 1) * DeliveriesPageSize).Take(DeliveriesPageSize)
            .Join(db.WebhookEvents, d => d.EventId, e => e.Id, (d, e) => new { d, e.Type, e.ContractId })
            .Join(db.WebhookEndpoints, x => x.d.EndpointId, w => w.Id, (x, w) => new { x.d, x.Type, x.ContractId, w.Url })
            .Select(x => new
            {
                x.d, x.Type, x.ContractId, x.Url,
                Number = db.Contracts.Where(c => c.Id == x.ContractId).Select(c => (int?)c.Number).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var items = rows.Select(r => new WebhookDeliveryDto(
            r.d.Id, r.d.EventId, r.Type, r.d.EndpointId, r.Url, r.d.Status, r.d.Attempts, r.d.LastStatusCode, r.d.LastError,
            r.d.ResponseBody, r.d.CreatedAt, r.d.LastAttemptAt, r.d.DeliveredAt,
            r.d.Status == WebhookDeliveryStatus.Pending ? r.d.NextAttemptAt : null, r.ContractId, r.Number)).ToList();
        return new Paged<WebhookDeliveryDto>(items, page, DeliveriesPageSize, total);
    }

    public async Task<Result> RedeliverAsync(long deliveryId, CancellationToken ct)
    {
        if (await GuardAsync(ct) is { } denied)
            return denied;
        var orgId = tenant.RequireOrganizationId();
        var delivery = await db.WebhookDeliveries.SingleOrDefaultAsync(d => d.Id == deliveryId && d.OrganizationId == orgId, ct);
        if (delivery is null)
            return Error.NotFound("Entrega não encontrada.");
        if (delivery.Status == WebhookDeliveryStatus.Pending)
            return Error.Conflict("Esta entrega ainda está na fila de tentativas.", "delivery_pending");
        var endpoint = await FindEndpointAsync(delivery.EndpointId, ct);
        if (endpoint is not { Enabled: true })
            return Error.Conflict("Ative o webhook antes de entregar de novo.", "endpoint_disabled");

        db.WebhookDeliveries.Add(new WebhookDelivery
        {
            OrganizationId = orgId, EventId = delivery.EventId, EndpointId = delivery.EndpointId, NextAttemptAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    // ---------- Domínios do embed ----------

    public async Task<Result<IReadOnlyList<EmbedOriginDto>>> ListEmbedOriginsAsync(CancellationToken ct)
    {
        if (!CanManageHooks)
            return AdminOnly;
        return Result<IReadOnlyList<EmbedOriginDto>>.Ok(await OriginsAsync(tenant.RequireOrganizationId(), ct));
    }

    public async Task<Result<EmbedOriginDto>> AddEmbedOriginAsync(EmbedOriginRequest request, CancellationToken ct)
    {
        var result = await AddOriginAsync(request, idempotent: false, ct);
        return result.IsSuccess ? result.Value!.Origin : result.Error!;
    }

    public Task<Result<EnsuredEmbedOrigin>> EnsureEmbedOriginAsync(EmbedOriginRequest request, CancellationToken ct) =>
        AddOriginAsync(request, idempotent: true, ct);

    private async Task<Result<EnsuredEmbedOrigin>> AddOriginAsync(EmbedOriginRequest request, bool idempotent, CancellationToken ct)
    {
        if (await GuardAsync(ct, allowApiKey: true) is { } denied)
            return denied;
        var orgId = tenant.RequireOrganizationId();
        var (origin, error) = EmbedOrigins.Normalize(request.Origin);
        if (error is not null)
            return Error.Validation("origin", error);
        // Antes do limite: repetir o cadastro com a lista cheia não é erro na API.
        if (await db.EmbedOrigins.Where(o => o.OrganizationId == orgId && o.Origin == origin).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct) is { } existing)
            return idempotent
                ? new EnsuredEmbedOrigin((await OriginsAsync(orgId, ct)).Single(o => o.Id == existing), false)
                : Error.Validation("origin", "Este endereço já está cadastrado.");
        if (await db.EmbedOrigins.CountAsync(o => o.OrganizationId == orgId, ct) >= EmbedOrigins.MaxPerOrganization)
            return Error.Conflict($"Sua conta já tem {EmbedOrigins.MaxPerOrganization} domínios. Remova um que não usa mais.", "too_many_origins");

        var entity = new EmbedOrigin
        {
            OrganizationId = orgId, Origin = origin!, CreatedBy = tenant.RequireUserId(), CreatedByApiKeyId = tenant.ApiKeyId,
            CreatedAt = clock.GetUtcNow(),
        };
        db.EmbedOrigins.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (idempotent)
        {
            // Duas chamadas ao mesmo tempo: o índice único segura a segunda, que devolve a primeira.
            db.ChangeTracker.Clear();
            var winner = (await OriginsAsync(orgId, ct)).FirstOrDefault(o => o.Origin == origin);
            if (winner is null)
                throw;
            return new EnsuredEmbedOrigin(winner, false);
        }
        logger.LogInformation("Domínio do embed {Origin} cadastrado na organização {OrganizationId} (chave {KeyId})", origin, orgId, tenant.ApiKeyId);
        return new EnsuredEmbedOrigin((await OriginsAsync(orgId, ct)).Single(o => o.Id == entity.Id), true);
    }

    public async Task<Result> DeleteEmbedOriginAsync(Guid id, CancellationToken ct)
    {
        if (!CanManageHooks)
            return AdminOnly;
        var orgId = tenant.RequireOrganizationId();
        var rows = await db.EmbedOrigins.Where(o => o.Id == id && o.OrganizationId == orgId).ExecuteDeleteAsync(ct);
        return rows == 0 ? Error.NotFound("Domínio não encontrado.") : Result.Ok();
    }

    private async Task<IReadOnlyList<EmbedOriginDto>> OriginsAsync(Guid orgId, CancellationToken ct) =>
        await db.EmbedOrigins.Where(o => o.OrganizationId == orgId).OrderBy(o => o.CreatedAt)
            .Select(o => new EmbedOriginDto(o.Id, o.Origin, db.Users.Where(u => u.Id == o.CreatedBy).Select(u => u.Name).FirstOrDefault(), o.CreatedAt,
                db.ApiKeys.Where(k => k.Id == o.CreatedByApiKeyId).Select(k => k.Name).FirstOrDefault()))
            .ToListAsync(ct);

    // ---------- Apoio ----------

    /// <summary>
    /// Webhooks e sites do embed: Admin na tela ou a API pública (chave). As chaves em si, só Admin.
    /// O papel da chave é Editor; o que a libera é o claim da chave, que só existe nas rotas da API pública.
    /// </summary>
    private bool CanManageHooks => tenant.IsAdmin() || tenant.ApiKeyId is not null;

    /// <summary>Admin (ou a chave, com <paramref name="allowApiKey"/>) e plano com API. Ver, revogar, desativar e excluir continuam liberados fora do plano.</summary>
    private async Task<Error?> GuardAsync(CancellationToken ct, bool allowApiKey = false)
    {
        if (!(allowApiKey ? CanManageHooks : tenant.IsAdmin()))
            return AdminOnly;
        var orgId = tenant.RequireOrganizationId();
        var planId = await db.Organizations.Where(o => o.Id == orgId).Select(o => o.PlanId).SingleAsync(ct);
        return Plans.Get(planId).ApiAccess ? null : NotInPlan();
    }

    private Task<WebhookEndpoint?> FindEndpointAsync(Guid id, CancellationToken ct)
    {
        var orgId = tenant.RequireOrganizationId();
        return db.WebhookEndpoints.SingleOrDefaultAsync(e => e.Id == id && e.OrganizationId == orgId, ct);
    }

    private async Task<IReadOnlyList<WebhookEndpointDto>> EndpointsAsync(Guid orgId, CancellationToken ct)
    {
        var rows = await db.WebhookEndpoints.AsNoTracking().Where(e => e.OrganizationId == orgId).OrderBy(e => e.CreatedAt)
            .Select(e => new
            {
                e,
                UserName = db.Users.Where(u => u.Id == e.CreatedBy).Select(u => u.Name).FirstOrDefault(),
                KeyName = db.ApiKeys.Where(k => k.Id == e.CreatedByApiKeyId).Select(k => k.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return rows.Select(r => ToDto(r.e) with { CreatedByName = r.UserName, CreatedByApiKeyName = r.KeyName }).ToList();
    }

    private ValidationErrors Validate(WebhookEndpointRequest request)
    {
        var errors = new ValidationErrors();
        if (WebhookUrls.Validate(request.Url, options.Value.AllowPrivateNetworks) is { } urlError)
            errors.Add("url", urlError);
        if (request.Description?.Trim() is { Length: > WebhookEndpoint.DescriptionMaxLength })
            errors.Add("description", $"Use no máximo {WebhookEndpoint.DescriptionMaxLength} caracteres.");
        foreach (var type in request.Events ?? [])
            if (!WebhookEventTypes.IsKnown(type))
                errors.Add("events", $"Evento desconhecido: {type}.");
        return errors;
    }

    /// <summary>Todos marcados é o mesmo que nenhum: recebe tudo, inclusive eventos que surgirem depois.</summary>
    private static List<string> NormalizeEvents(List<string>? events)
    {
        var list = (events ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return list.Count == WebhookEventTypes.All.Count ? [] : list;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static WebhookEndpointDto ToDto(WebhookEndpoint e) => new(
        e.Id, e.Url, e.Description, e.Events, e.Enabled, e.CreatedAt, e.LastSuccessAt, e.LastFailureAt, e.FailureStreak);
}

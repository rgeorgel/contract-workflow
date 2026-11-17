using Contratos.Application.Common;

namespace Contratos.Application.Integrations;

// ---------- Chaves de API ----------

/// <param name="Prefix">Começo da chave, para reconhecer qual é qual (a chave inteira só aparece na criação).</param>
public sealed record ApiKeyDto(
    Guid Id, string Name, string Prefix, string? CreatedByName, DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt);

/// <param name="Token">A chave completa. Mostrada uma única vez: o sistema guarda só o hash.</param>
public sealed record CreatedApiKeyDto(ApiKeyDto Key, string Token);

public sealed record CreateApiKeyRequest(string? Name);

// ---------- Webhooks ----------

/// <param name="Events">Tipos assinados; vazio recebe todos.</param>
/// <param name="CreatedByApiKeyName">Nome da chave, quando o webhook foi cadastrado pela API pública.</param>
public sealed record WebhookEndpointDto(
    Guid Id, string Url, string? Description, IReadOnlyList<string> Events, bool Enabled,
    DateTimeOffset CreatedAt, DateTimeOffset? LastSuccessAt, DateTimeOffset? LastFailureAt, int FailureStreak,
    string? CreatedByName = null, string? CreatedByApiKeyName = null);

public sealed record CreatedWebhookEndpointDto(WebhookEndpointDto Endpoint, string Secret);

/// <param name="Enabled">Nulo mantém como está (na criação, ativo).</param>
public sealed record WebhookEndpointRequest(string? Url, string? Description, List<string>? Events, bool? Enabled);

public sealed record WebhookSecretDto(string Secret);

public sealed record WebhookEventTypeDto(string Type, string Description);

/// <param name="Status">pending (na fila ou aguardando nova tentativa), succeeded ou failed (desistiu).</param>
public sealed record WebhookDeliveryDto(
    long Id, Guid EventId, string EventType, Guid EndpointId, string EndpointUrl, string Status, int Attempts,
    int? LastStatusCode, string? LastError, string? ResponseBody, DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptAt, DateTimeOffset? DeliveredAt, DateTimeOffset? NextAttemptAt,
    Guid? ContractId, int? ContractNumber);

/// <param name="Available">O plano atual inclui API e webhooks.</param>
/// <param name="PlansWithApi">Nomes dos planos que incluem, para o convite de mudar de plano.</param>
public sealed record IntegrationsOverviewDto(
    bool Available, IReadOnlyList<string> PlansWithApi, IReadOnlyList<ApiKeyDto> Keys,
    IReadOnlyList<WebhookEndpointDto> Endpoints, IReadOnlyList<WebhookEventTypeDto> EventTypes,
    int MaxKeys, int MaxEndpoints, IReadOnlyList<EmbedOriginDto> EmbedOrigins, int MaxEmbedOrigins);

public static class IntegrationErrors
{
    public static IReadOnlyList<string> PlansWithApi() =>
        Domain.Billing.Plans.All.Where(p => p.ApiAccess).Select(p => p.Name).ToList();

    public static Error NotInPlan() => Error.Forbidden(
        $"A API e os webhooks fazem parte do plano {string.Join(" e ", PlansWithApi())}. Mude de plano em Configurações.",
        "api_not_in_plan");
}

/// <summary>
/// Chaves de API e webhooks da organização. Só Admin; webhooks e sites do embed também pela API pública
/// (a chave já dá acesso a todos os contratos, então não abre dados novos).
/// </summary>
public interface IIntegrationService
{
    /// <summary>Conta, plano e uso do ciclo, para a API pública (<c>GET /account</c>). Qualquer chave.</summary>
    Task<PublicAccount> GetAccountAsync(CancellationToken ct);

    Task<Result<IntegrationsOverviewDto>> GetAsync(CancellationToken ct);
    Task<Result<CreatedApiKeyDto>> CreateKeyAsync(CreateApiKeyRequest request, CancellationToken ct);
    Task<Result> RevokeKeyAsync(Guid id, CancellationToken ct);

    Task<Result<IReadOnlyList<WebhookEndpointDto>>> ListEndpointsAsync(CancellationToken ct);
    Task<Result<CreatedWebhookEndpointDto>> CreateEndpointAsync(WebhookEndpointRequest request, CancellationToken ct);
    Task<Result<WebhookEndpointDto>> UpdateEndpointAsync(Guid id, WebhookEndpointRequest request, CancellationToken ct);
    Task<Result> DeleteEndpointAsync(Guid id, CancellationToken ct);
    Task<Result<WebhookSecretDto>> GetSecretAsync(Guid id, CancellationToken ct);
    Task<Result<WebhookSecretDto>> RotateSecretAsync(Guid id, CancellationToken ct);
    /// <summary>Manda um evento <c>webhook.test</c> só para este endpoint.</summary>
    Task<Result> SendTestAsync(Guid id, CancellationToken ct);

    Task<Result<Paged<WebhookDeliveryDto>>> ListDeliveriesAsync(Guid? endpointId, int page, CancellationToken ct);
    /// <summary>Entrega de novo um evento (nova entrega, com as tentativas do zero).</summary>
    Task<Result> RedeliverAsync(long deliveryId, CancellationToken ct);

    Task<Result<IReadOnlyList<EmbedOriginDto>>> ListEmbedOriginsAsync(CancellationToken ct);
    /// <summary>Site que pode mostrar o embed de templates num iframe. Já cadastrado: erro de validação.</summary>
    Task<Result<EmbedOriginDto>> AddEmbedOriginAsync(EmbedOriginRequest request, CancellationToken ct);
    /// <summary>Como <see cref="AddEmbedOriginAsync"/>, mas devolve o existente se o site já estiver cadastrado (API pública).</summary>
    Task<Result<EnsuredEmbedOrigin>> EnsureEmbedOriginAsync(EmbedOriginRequest request, CancellationToken ct);
    /// <summary>Remove o site; as sessões abertas nele param de carregar.</summary>
    Task<Result> DeleteEmbedOriginAsync(Guid id, CancellationToken ct);
}

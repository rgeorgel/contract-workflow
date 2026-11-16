using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Contratos.Application.Contracts;
using Contratos.Domain.Contracts;
using Contratos.Domain.Templates;

namespace Contratos.Application.Integrations;

/// <summary>
/// JSON da API pública e dos webhooks: propriedades em camelCase e enums em snake_case
/// (<c>partially_signed</c>, <c>sign_as_witness</c>). A API interna da tela continua com o formato dela.
/// </summary>
public static class PublicJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        };
        o.MakeReadOnly(populateMissingResolver: true);
        return o;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>"SignerSigned" → "signer_signed" (tipos da linha do tempo na API pública).</summary>
    public static string Snake(string pascal) => JsonNamingPolicy.SnakeCaseLower.ConvertName(pascal);
}

/// <summary>
/// Assinatura dos webhooks, no mesmo esquema do Stripe: <c>t={unix},v1={hex(HMAC-SHA256(segredo, "{t}.{corpo}"))}</c>.
/// O timestamp no texto assinado impede reaproveitar uma entrega antiga.
/// </summary>
public static class WebhookSignature
{
    public const string Header = "X-Contratos-Signature";

    public static string Compute(string secret, long timestamp, string body)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(mac)}";
    }
}

public sealed record PublicTemplateRef(Guid Id, string Name, int Version);

public sealed record PublicSigner(
    Guid Id, string Name, string? Email, string? Phone, string? Cpf, DeliveryMethod DeliveryMethod,
    string? Role, SignerAction Action, int SignOrder, SignerStatus Status, string? SignLink,
    DateTimeOffset? ViewedAt, DateTimeOffset? SignedAt, DateTimeOffset? DeclinedAt, string? DeclineReason);

public sealed record PublicContract(
    Guid Id, int Number, string Title, ContractStatus Status, string? ExternalReference, PublicTemplateRef Template,
    SigningMode SigningMode, DateTimeOffset? ExpiresAt, string? Message, IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<PublicSigner> Signers, string? DocumentSha256, bool HasDocument, bool HasSignedDocument,
    string? SendError, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? SentAt, DateTimeOffset? CompletedAt, DateTimeOffset? CancelledAt);

public sealed record PublicContractListItem(
    Guid Id, int Number, string Title, ContractStatus Status, string? ExternalReference, string TemplateName,
    int SignersTotal, int SignersSigned, bool SendError, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, DateTimeOffset? CompletedAt);

public sealed record PublicPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total, bool HasMore);

public sealed record PublicVariable(string Key, string Label, VariableType Type, bool Required);
public sealed record PublicSignerRole(string Role, SignerAction Action, int Order);

public sealed record PublicTemplateListItem(Guid Id, string Name, string? Description, int Version, int VariableCount, DateTimeOffset UpdatedAt);

public sealed record PublicTemplate(
    Guid Id, string Name, string? Description, int Version, IReadOnlyList<PublicVariable> Variables,
    IReadOnlyList<PublicSignerRole> SuggestedSigners, DateTimeOffset UpdatedAt);

public sealed record PublicEvent(
    long Id, string Type, DateTimeOffset OccurredAt, string? SignerName, string? ActorName, IReadOnlyDictionary<string, string?> Details);

public sealed record PublicAccount(PublicOrganization Organization, PublicPlanUsage Usage);
public sealed record PublicOrganization(string Name, string Slug, string PlanId, string PlanName);

/// <param name="AvailableSends">Envios que ainda cabem: cota do ciclo que sobra mais os créditos.</param>
public sealed record PublicPlanUsage(
    DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd, int ContractsUsed, int ContractsLimit,
    int CreditsAvailable, int AvailableSends, bool PaymentOverdue);

// ---------- Corpo das requisições ----------

public sealed record PublicSignerInput(
    string? Name, string? Email, string? Phone, string? Cpf, DeliveryMethod? DeliveryMethod,
    string? Role, SignerAction? Action, int? SignOrder);

/// <param name="Send">Envia logo depois de criar. Sem ele, o contrato fica como rascunho.</param>
public sealed record PublicCreateContractRequest(
    Guid? TemplateId, string? Title, Dictionary<string, string>? Variables, List<PublicSignerInput>? Signers,
    SigningMode? SigningMode, DateOnly? ExpiresAt, string? Message, string? ExternalReference, bool Send);

// ---------- Webhooks ----------

/// <param name="Reason">Motivo da recusa (contract.declined) ou do erro (contract.send_failed).</param>
public sealed record WebhookData(PublicContract? Contract, PublicSigner? Signer, string? Reason, string? Message = null);

public sealed record WebhookPayload(Guid Id, string Type, DateTimeOffset CreatedAt, WebhookData Data);

public static class PublicMapper
{
    public static PublicContract Contract(ContractDetail c) => new(
        c.Id, c.Number, c.Title, c.Status, c.ExternalReference,
        new PublicTemplateRef(c.Template.Id, c.Template.Name, c.Template.Version),
        c.SigningMode, c.ExpiresAt, c.Message, c.VariableValues,
        c.Signers.Select(s => new PublicSigner(s.Id, s.Name, s.Email, s.Phone, s.Cpf, s.DeliveryMethod, s.Role, s.Action,
            s.SignOrder, s.Status, s.ShortLink, s.ViewedAt, s.SignedAt, s.DeclinedAt, s.DeclineReason)).ToList(),
        c.DocumentSha256, c.HasDocument, c.HasSignedDocument, c.SendError,
        c.CreatedAt, c.UpdatedAt, c.SentAt, c.CompletedAt, c.CancelledAt);

    public static PublicContract Contract(Contract c, PublicTemplateRef template) => new(
        c.Id, c.Number, c.Title, c.Status, c.ExternalReference, template, c.SigningMode, c.ExpiresAt, c.Message,
        c.VariableValues, c.SignersInSendOrder().Select(Signer).ToList(), c.DocumentSha256,
        c.DocumentKey is not null, c.SignedDocumentKey is not null, c.SendError,
        c.CreatedAt, c.UpdatedAt, c.SentAt, c.CompletedAt, c.CancelledAt);

    public static PublicSigner Signer(ContractSigner s) => new(
        s.Id, s.Name, s.Email, s.Phone, s.Cpf, s.DeliveryMethod, s.Role, s.Action, s.SignOrder, s.Status, s.ShortLink,
        s.ViewedAt, s.SignedAt, s.DeclinedAt, s.DeclineReason);

    public static PublicContractListItem ListItem(ContractListItem c) => new(
        c.Id, c.Number, c.Title, c.Status, c.ExternalReference, c.TemplateName, c.SignersTotal, c.SignersSigned,
        c.SendError, c.CreatedAt, c.SentAt, c.CompletedAt);
}

/// <summary>Eventos de webhook: nome público e de qual evento da linha do tempo cada um nasce.</summary>
public static class WebhookEventTypes
{
    public const string Test = "webhook.test";

    public static IReadOnlyList<WebhookEventTypeDto> All { get; } =
    [
        new("contract.created", "Contrato criado (pela tela, pela API ou em lote)."),
        new("contract.sent", "Contrato entregue ao provedor de assinatura; os links de assinatura já existem."),
        new("contract.send_failed", "O envio falhou depois das tentativas; o contrato precisa de correção."),
        new("signer.viewed", "Um signatário abriu o documento."),
        new("signer.signed", "Um signatário assinou."),
        new("signer.delivery_failed", "O link não chegou a um signatário (e-mail ou telefone inválido)."),
        new("contract.declined", "Um signatário recusou; o contrato não pode mais ser assinado."),
        new("contract.completed", "Todos assinaram."),
        new("contract.signed_document_ready", "O PDF assinado está disponível para download."),
        new("contract.expired", "O prazo para assinar terminou sem todas as assinaturas."),
        new("contract.cancelled", "O contrato foi cancelado."),
    ];

    private static readonly Dictionary<string, string> FromTimeline = new(StringComparer.Ordinal)
    {
        [ContractEventTypes.Created] = "contract.created",
        [ContractEventTypes.SentToProvider] = "contract.sent",
        [ContractEventTypes.SendFailed] = "contract.send_failed",
        [ContractEventTypes.SignerViewed] = "signer.viewed",
        [ContractEventTypes.SignerSigned] = "signer.signed",
        [ContractEventTypes.DeliveryFailed] = "signer.delivery_failed",
        [ContractEventTypes.SignerDeclined] = "contract.declined",
        [ContractEventTypes.Completed] = "contract.completed",
        [ContractEventTypes.SignedDocumentStored] = "contract.signed_document_ready",
        [ContractEventTypes.Expired] = "contract.expired",
        [ContractEventTypes.Cancelled] = "contract.cancelled",
    };

    public static string? FromContractEvent(string timelineType) => FromTimeline.GetValueOrDefault(timelineType);

    public static bool IsKnown(string type) => All.Any(t => t.Type == type);
}

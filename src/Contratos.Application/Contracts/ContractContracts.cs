using Contratos.Application.Common;
using Contratos.Domain.Contracts;
using Contratos.Domain.Templates;

namespace Contratos.Application.Contracts;

public sealed record ContractListQuery(
    string? Q, ContractStatus? Status, Guid? TemplateId, DateOnly? From, DateOnly? To,
    int Page = 1, int PageSize = 20, string? Sort = null);

public sealed record ContractListItem(
    Guid Id, int Number, string Title, ContractStatus Status, string TemplateName,
    int SignersTotal, int SignersSigned, DateTimeOffset CreatedAt, DateTimeOffset? SentAt,
    DateTimeOffset? CompletedAt, bool SendError, int? PendingDays);

public sealed record SignerDto(
    Guid Id, string Name, string? Email, string? Phone, string? Cpf, DeliveryMethod DeliveryMethod,
    string? Role, SignerAction Action, int SignOrder, SignerStatus Status, string? ShortLink,
    DateTimeOffset? ViewedAt, DateTimeOffset? SignedAt, DateTimeOffset? DeclinedAt, string? DeclineReason, bool CanResend);

public sealed record ContractTemplateInfo(Guid Id, string Name, Guid VersionId, int Version,
    IReadOnlyList<TemplateVariable> Variables, IReadOnlyList<SignerRoleSuggestion> SuggestedRoles);

public sealed record ContractPermissions(bool CanEdit, bool CanSend, bool CanCancel, bool CanDelete, bool CanResend, bool CanSimulate);

public sealed record ContractDetail(
    Guid Id, int Number, string Title, ContractStatus Status, SigningMode SigningMode,
    DateTimeOffset? ExpiresAt, string? Message, IReadOnlyDictionary<string, string> VariableValues,
    ContractTemplateInfo Template, IReadOnlyList<SignerDto> Signers,
    bool HasDocument, bool HasSignedDocument, string? DocumentSha256, string? SendError,
    string? CreatedByName, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? SentAt, DateTimeOffset? CompletedAt, DateTimeOffset? CancelledAt,
    ContractPermissions Permissions, uint RowVersion);

public sealed record ContractEventDto(
    long Id, string Type, DateTimeOffset OccurredAt, string? SignerName, string? ActorName,
    IReadOnlyDictionary<string, string?> Payload);

public sealed record CreateContractRequest(Guid TemplateId, string? Title);
public sealed record UpdateContractRequest(
    string? Title, Dictionary<string, string>? VariableValues, DateOnly? ExpiresAt, SigningMode? SigningMode, string? Message);
public sealed record SignerRequest(
    string? Name, string? Email, string? Phone, string? Cpf, DeliveryMethod? DeliveryMethod,
    string? Role, SignerAction? Action, int? SignOrder);

public sealed record FileResult(Stream Content, string FileName);

public interface IContractService
{
    Task<Paged<ContractListItem>> ListAsync(ContractListQuery query, CancellationToken ct);
    Task<Result<ContractDetail>> GetAsync(Guid id, CancellationToken ct);
    Task<Result<IReadOnlyList<ContractEventDto>>> GetEventsAsync(Guid id, CancellationToken ct);
    Task<Result<ContractDetail>> CreateAsync(CreateContractRequest req, CancellationToken ct);
    Task<Result<ContractDetail>> UpdateAsync(Guid id, UpdateContractRequest req, uint? expectedRowVersion, CancellationToken ct);
    Task<Result<ContractDetail>> AddSignerAsync(Guid id, SignerRequest req, CancellationToken ct);
    Task<Result<ContractDetail>> UpdateSignerAsync(Guid id, Guid signerId, SignerRequest req, CancellationToken ct);
    Task<Result<ContractDetail>> RemoveSignerAsync(Guid id, Guid signerId, CancellationToken ct);
    Task<Result<ContractDetail>> SendAsync(Guid id, string? idempotencyKey, CancellationToken ct);
    Task<Result<ContractDetail>> RetrySendAsync(Guid id, CancellationToken ct);
    Task<Result<ContractDetail>> CancelAsync(Guid id, CancellationToken ct);
    Task<Result<ContractDetail>> ResendAsync(Guid id, Guid signerId, CancellationToken ct);
    Task<Result> DeleteAsync(Guid id, int confirmNumber, bool alsoInProvider, CancellationToken ct);
    Task<Result<FileResult>> GetDocumentAsync(Guid id, bool signed, CancellationToken ct);
    Task<Result<string>> PreviewAsync(Guid id, CancellationToken ct);
}

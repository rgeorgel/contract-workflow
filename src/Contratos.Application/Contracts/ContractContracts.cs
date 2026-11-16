using Contratos.Application.Common;
using Contratos.Domain.Contracts;
using Contratos.Domain.Templates;

namespace Contratos.Application.Contracts;

/// <param name="DateField">"created" (padrão) ou "completed": a qual data From/To se aplicam.</param>
public sealed record ContractListQuery(
    string? Q, ContractStatus? Status, Guid? TemplateId, DateOnly? From, DateOnly? To,
    int Page = 1, int PageSize = 20, string? Sort = null, string? DateField = null, Guid? CreatedBy = null,
    string? ExternalReference = null);

public sealed record ContractListItem(
    Guid Id, int Number, string Title, ContractStatus Status, string TemplateName,
    int SignersTotal, int SignersSigned, DateTimeOffset CreatedAt, DateTimeOffset? SentAt,
    DateTimeOffset? CompletedAt, bool SendError, int? PendingDays, string? ExternalReference = null);

public sealed record SignerDto(
    Guid Id, string Name, string? Email, string? Phone, string? Cpf, DeliveryMethod DeliveryMethod,
    string? Role, SignerAction Action, int SignOrder, SignerStatus Status, string? ShortLink,
    DateTimeOffset? ViewedAt, DateTimeOffset? SignedAt, DateTimeOffset? DeclinedAt, string? DeclineReason, bool CanResend);

public sealed record ContractTemplateInfo(Guid Id, string Name, Guid VersionId, int Version,
    IReadOnlyList<TemplateVariable> Variables, IReadOnlyList<SignerRoleSuggestion> SuggestedRoles);

public sealed record ContractPermissions(bool CanEdit, bool CanSend, bool CanCancel, bool CanDelete, bool CanResend, bool CanSimulate, bool CanReturnToDraft);

public sealed record ContractDetail(
    Guid Id, int Number, string Title, ContractStatus Status, SigningMode SigningMode,
    DateTimeOffset? ExpiresAt, string? Message, IReadOnlyDictionary<string, string> VariableValues,
    ContractTemplateInfo Template, IReadOnlyList<SignerDto> Signers,
    bool HasDocument, bool HasSignedDocument, string? DocumentSha256, string? SendError,
    string? CreatedByName, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? SentAt, DateTimeOffset? CompletedAt, DateTimeOffset? CancelledAt,
    ContractPermissions Permissions, uint RowVersion, string? ExternalReference = null);

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

/// <summary>Uma linha da planilha do envio em lote: um contrato com seus signatários.</summary>
public sealed record BulkContractRow(
    string? Title, Dictionary<string, string>? VariableValues, DateOnly? ExpiresAt, string? Message,
    SigningMode? SigningMode, List<SignerRequest>? Signers);

/// <param name="DryRun">Só valida (prévia); sem ele, cria os rascunhos se todas as linhas estiverem certas.</param>
public sealed record BulkContractsRequest(Guid TemplateId, List<BulkContractRow>? Rows, bool DryRun);

/// <param name="Row">Posição na lista enviada, a partir de 1.</param>
/// <param name="Errors">Por campo: title, expiresAt, variableValues.chave, signers, signers[0].email...</param>
public sealed record BulkRowResult(int Row, Guid? ContractId, int? Number, IDictionary<string, string[]> Errors);

public sealed record BulkContractsResult(int Total, int Valid, bool Created, IReadOnlyList<BulkRowResult> Rows);

/// <summary>Contrato completo numa chamada só (API pública): dados, signatários e referência externa.</summary>
public sealed record CompleteContractRequest(Guid TemplateId, BulkContractRow Row, string? ExternalReference);

/// <param name="Replayed">Mesma Idempotency-Key de antes: é o contrato criado na primeira chamada.</param>
public sealed record CreatedContract(ContractDetail Contract, bool Replayed);

public interface IContractService
{
    /// <summary>Valida e cria rascunhos em lote (planos com envio em lote). Tudo ou nada: com uma linha errada, nada é criado.</summary>
    Task<Result<BulkContractsResult>> CreateBulkAsync(BulkContractsRequest request, CancellationToken ct);

    /// <summary>Cria o rascunho já preenchido, validando tudo antes (como uma linha do lote). Aceita Idempotency-Key.</summary>
    Task<Result<CreatedContract>> CreateCompleteAsync(CompleteContractRequest request, string? idempotencyKey, CancellationToken ct);

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
    /// <summary>Envio que falhou antes de chegar ao provedor volta a rascunho, para corrigir e enviar de novo.</summary>
    Task<Result<ContractDetail>> ReturnToDraftAsync(Guid id, CancellationToken ct);
    Task<Result<ContractDetail>> CancelAsync(Guid id, CancellationToken ct);
    Task<Result<ContractDetail>> ResendAsync(Guid id, Guid signerId, CancellationToken ct);
    Task<Result> DeleteAsync(Guid id, int confirmNumber, bool alsoInProvider, CancellationToken ct);
    Task<Result<FileResult>> GetDocumentAsync(Guid id, bool signed, CancellationToken ct);
    Task<Result<string>> PreviewAsync(Guid id, CancellationToken ct);
}

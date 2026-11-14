using Contratos.Domain.Contracts;
using Contratos.Domain.Templates;

namespace Contratos.Application.Signatures;

public sealed record DocumentSigner(string Name, string? Email, string? Phone, DeliveryMethod DeliveryMethod, SignerAction Action, string? Cpf);

public sealed record DocumentRequest(
    string Name, string? Message, bool Sortable, DateTimeOffset? DeadlineAt, string? Reminder, string? FolderId,
    IReadOnlyList<DocumentSigner> Signers);

/// <summary>Assinaturas na mesma ordem de <see cref="DocumentRequest.Signers"/>.</summary>
public sealed record CreatedDocument(string DocumentId, IReadOnlyList<CreatedSignature> Signatures);
public sealed record CreatedSignature(string PublicId, string? ShortLink);

public enum SignatureEventType { Viewed, Signed, Declined, DeliveryFailed, DocumentFinished, DocumentDeleted }

public sealed record SignatureEvent(
    string ExternalEventId, SignatureEventType Type, string DocumentId, string? SignaturePublicId,
    string? Reason, DateTimeOffset OccurredAt);

public sealed record DocumentStatus(string DocumentId, bool Finished, IReadOnlyList<SignatureEvent> Events);

/// <summary>
/// Provedor de assinatura. O domínio não conhece a Autentique: ela entra na fase 4 por trás desta interface;
/// até lá o <c>FakeSignatureProvider</c> simula tudo.
/// </summary>
public interface ISignatureProvider
{
    string Name { get; }
    Task<CreatedDocument> CreateDocumentAsync(DocumentRequest req, Stream pdf, CancellationToken ct);
    Task DeleteDocumentAsync(string documentId, CancellationToken ct);
    Task ResendAsync(IReadOnlyList<string> signaturePublicIds, CancellationToken ct);
    Task<DocumentStatus> GetDocumentAsync(string documentId, CancellationToken ct);
    Task<Stream> DownloadSignedPdfAsync(string documentId, CancellationToken ct);
    bool ValidateWebhook(string rawBody, string? signatureHeader);
    IReadOnlyList<SignatureEvent> ParseWebhook(string rawBody);
}

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
/// Saldo da conta da plataforma no provedor: documentos restantes e créditos de verificação extra (SMS, biometria).
/// Campos nulos: o provedor não informou.
/// </summary>
public sealed record ProviderBalance(int? Documents, int? Credits);

/// <summary>
/// Falha do provedor. <see cref="Permanent"/>: repetir não adianta (dados recusados); a mensagem é para o usuário.
/// <see cref="OutOfCredits"/>: a conta da plataforma ficou sem saldo.
/// </summary>
public class SignatureProviderException(string message, bool permanent = false, bool outOfCredits = false, Exception? inner = null)
    : Exception(message, inner)
{
    public bool Permanent { get; } = permanent;
    public bool OutOfCredits { get; } = outOfCredits;
}

/// <summary>
/// Provedor de assinatura. O domínio não conhece a Autentique: ela fica por trás desta interface
/// (<c>AutentiqueSignatureProvider</c>); o <c>FakeSignatureProvider</c> simula tudo em desenvolvimento e testes.
/// </summary>
public interface ISignatureProvider
{
    string Name { get; }
    Task<CreatedDocument> CreateDocumentAsync(DocumentRequest req, Stream pdf, CancellationToken ct);
    Task DeleteDocumentAsync(string documentId, CancellationToken ct);
    Task ResendAsync(IReadOnlyList<string> signaturePublicIds, CancellationToken ct);
    Task<DocumentStatus> GetDocumentAsync(string documentId, CancellationToken ct);
    Task<Stream> DownloadSignedPdfAsync(string documentId, CancellationToken ct);
    /// <summary>Saldo da conta; null quando o provedor não tem saldo (fake).</summary>
    Task<ProviderBalance?> GetBalanceAsync(CancellationToken ct);
    bool ValidateWebhook(string rawBody, string? signatureHeader);
    IReadOnlyList<SignatureEvent> ParseWebhook(string rawBody);
}

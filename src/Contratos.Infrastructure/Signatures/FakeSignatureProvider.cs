using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Contratos.Application.Signatures;
using Contratos.Application.Storage;
using Contratos.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Signatures;

public sealed class FakeSignatureOptions
{
    public const string Section = "Signature:Fake";
    public string WebhookSecret { get; set; } = "fake-webhook-secret";
}

/// <summary>
/// Provedor de mentira, para desenvolvimento e testes: aceita o documento, guarda o PDF no próprio storage
/// e recebe "webhooks" no mesmo formato que o simulador da tela de detalhe gera.
/// Um título contendo <c>#falha-envio</c> faz a criação falhar (temporário, exercita as retentativas);
/// <c>#recusa</c> simula dados recusados (permanente, vai direto para "Erro no envio").
/// </summary>
public sealed class FakeSignatureProvider(IFileStorage storage, IOptions<FakeSignatureOptions> options) : ISignatureProvider
{
    public const string ProviderName = "fake";
    public const string FailMarker = "#falha-envio";
    public const string RejectMarker = "#recusa";

    public string Name => ProviderName;

    public async Task<CreatedDocument> CreateDocumentAsync(DocumentRequest req, Stream pdf, CancellationToken ct)
    {
        if (req.Name.Contains(FailMarker, StringComparison.Ordinal))
            throw new HttpRequestException("Falha simulada do provedor fake.");
        if (req.Name.Contains(RejectMarker, StringComparison.Ordinal))
            throw new SignatureProviderException(
                $"O provedor recusou o e-mail de {req.Signers[0].Name} (formato inválido, simulado). Volte o contrato para rascunho, corrija e envie de novo.",
                permanent: true);

        var documentId = $"fake-doc-{Guid.NewGuid():N}";
        await storage.PutAsync(Key(documentId), pdf, "application/pdf", ct);
        var signatures = req.Signers.Select(_ => new CreatedSignature($"fake-sig-{Guid.NewGuid():N}", null)).ToList();
        return new CreatedDocument(documentId, signatures);
    }

    public Task DeleteDocumentAsync(string documentId, CancellationToken ct) => storage.DeletePrefixAsync(Key(documentId), ct);

    public Task<bool> ResendAsync(IReadOnlyList<string> signaturePublicIds, CancellationToken ct) => Task.FromResult(true);

    public Task<DocumentStatus> GetDocumentAsync(string documentId, CancellationToken ct) =>
        Task.FromResult(new DocumentStatus(documentId, false, []));

    public Task<ProviderBalance?> GetBalanceAsync(CancellationToken ct) => Task.FromResult<ProviderBalance?>(null);

    public async Task<Stream> DownloadSignedPdfAsync(string documentId, CancellationToken ct) =>
        await storage.OpenReadAsync(Key(documentId), ct) ?? throw new InvalidOperationException("Documento fake não encontrado.");

    public bool ValidateWebhook(string rawBody, string? signatureHeader) =>
        signatureHeader is not null && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Sign(rawBody, options.Value.WebhookSecret)),
            Encoding.ASCII.GetBytes(signatureHeader.Trim().ToLowerInvariant()));

    public IReadOnlyList<SignatureEvent> ParseWebhook(string rawBody) =>
        [JsonSerializer.Deserialize<SignatureEvent>(rawBody, JsonColumn.Options)
            ?? throw new JsonException("Corpo vazio.")];

    /// <summary>Monta um webhook assinado, como o simulador faria do lado do provedor.</summary>
    public (string Body, string Signature) BuildWebhook(SignatureEvent ev)
    {
        var body = JsonSerializer.Serialize(ev, JsonColumn.Options);
        return (body, Sign(body, options.Value.WebhookSecret));
    }

    internal static string Sign(string body, string secret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    private static string Key(string documentId) => $"fake-provider/{documentId}.pdf";
}

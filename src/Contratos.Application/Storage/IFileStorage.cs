namespace Contratos.Application.Storage;

/// <summary>Arquivos (PDFs) num bucket privado S3-compatível.</summary>
public interface IFileStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct);
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);
    Task DeletePrefixAsync(string prefix, CancellationToken ct);
}

public static class StorageKeys
{
    public static string ContractPrefix(Guid organizationId, Guid contractId) => $"org/{organizationId}/contracts/{contractId}/";
    public static string Original(Guid organizationId, Guid contractId) => ContractPrefix(organizationId, contractId) + "original.pdf";
    public static string Signed(Guid organizationId, Guid contractId) => ContractPrefix(organizationId, contractId) + "signed.pdf";
}

/// <summary>HTML → PDF (Gotenberg).</summary>
public interface IPdfRenderer
{
    Task<byte[]> RenderAsync(string html, CancellationToken ct);
}

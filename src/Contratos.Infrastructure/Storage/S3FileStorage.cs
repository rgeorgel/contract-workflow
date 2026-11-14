using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Contratos.Application.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";
    public string ServiceUrl { get; set; } = "http://localhost:8333";
    public string Bucket { get; set; } = "contratos";
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string Region { get; set; } = "us-east-1";
    /// <summary>Cria o bucket na subida se ele não existir.</summary>
    public bool CreateBucket { get; set; } = true;
}

/// <summary>Qualquer S3: AWS, Cloudflare R2, Wasabi, DigitalOcean Spaces ou o SeaweedFS local.</summary>
internal sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly AmazonS3Client _s3;
    private readonly StorageOptions _options;

    public S3FileStorage(IOptions<StorageOptions> options)
    {
        _options = options.Value;
        var config = new AmazonS3Config
        {
            ServiceURL = _options.ServiceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = _options.Region,
            // Nem todo S3-compatível aceita os checksums CRC que o SDK manda por padrão.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        _s3 = new AmazonS3Client(new BasicAWSCredentials(_options.AccessKey ?? "local", _options.SecretKey ?? "local"), config);
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        // O S3 precisa do tamanho; streams de rede (download do provedor, outro GetObject) não têm.
        // PDFs vão até 20 MB, então bufferizar em memória é aceitável.
        Stream body = content;
        MemoryStream? buffer = null;
        if (!content.CanSeek)
        {
            buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            body = buffer;
        }

        try
        {
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _options.Bucket,
                Key = key,
                InputStream = body,
                ContentType = contentType,
                AutoCloseStream = false,
                UseChunkEncoding = false,
            }, ct);
        }
        finally
        {
            buffer?.Dispose();
        }
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        try
        {
            var res = await _s3.GetObjectAsync(_options.Bucket, key, ct);
            return res.ResponseStream;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeletePrefixAsync(string prefix, CancellationToken ct)
    {
        string? token = null;
        do
        {
            var page = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _options.Bucket, Prefix = prefix, ContinuationToken = token,
            }, ct);
            foreach (var obj in page.S3Objects ?? [])
                await _s3.DeleteObjectAsync(_options.Bucket, obj.Key, ct);
            token = page.IsTruncated == true ? page.NextContinuationToken : null;
        } while (token is not null);
    }

    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        try
        {
            await _s3.PutBucketAsync(new PutBucketRequest { BucketName = _options.Bucket }, ct);
        }
        catch (AmazonS3Exception e) when (e.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
        }
    }

    public void Dispose() => _s3.Dispose();
}

/// <summary>Cria o bucket na primeira subida. Falha só gera aviso: o storage pode estar subindo ainda.</summary>
internal sealed class StorageInitializer(IFileStorage storage, IOptions<StorageOptions> options, ILogger<StorageInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        if (!options.Value.CreateBucket || storage is not S3FileStorage s3)
            return;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await s3.EnsureBucketAsync(ct);
                return;
            }
            catch (Exception e) when (e is AmazonServiceException or HttpRequestException)
            {
                logger.LogWarning(e, "Bucket ainda indisponível (tentativa {Attempt})", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

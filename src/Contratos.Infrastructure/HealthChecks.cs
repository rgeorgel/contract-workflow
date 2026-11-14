using Contratos.Application.Storage;
using Contratos.Infrastructure.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure;

/// <summary>Gotenberg responde em /health.</summary>
internal sealed class GotenbergHealthCheck(IHttpClientFactory http, IOptions<GotenbergOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var client = http.CreateClient(nameof(GotenbergHealthCheck));
            client.Timeout = TimeSpan.FromSeconds(3);
            using var res = await client.GetAsync(new Uri(new Uri(options.Value.Url), "/health"), ct);
            return res.IsSuccessStatusCode ? HealthCheckResult.Healthy() : new HealthCheckResult(context.Registration.FailureStatus, $"HTTP {(int)res.StatusCode}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Sem resposta");
        }
    }
}

/// <summary>Bucket acessível (lista no máximo um objeto).</summary>
internal sealed class StorageHealthCheck(IFileStorage storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (storage is not S3FileStorage s3)
            return HealthCheckResult.Healthy("Storage em memória");
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            await s3.PingAsync(cts.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Bucket inacessível");
        }
    }
}

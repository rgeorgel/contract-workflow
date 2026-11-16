using System.Text;
using Contratos.Application.Integrations;
using Contratos.Infrastructure.Messaging;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Integrations;

public sealed record WebhookFailingMessage(Guid OrganizationId, Guid EndpointId, string Error);

/// <summary>
/// Entrega os webhooks pendentes: POST com o corpo assinado, até <see cref="WebhookOptions.Concurrency"/> ao mesmo
/// tempo, e nova tentativa com espera crescente até desistir. Qualquer resposta 2xx conta como recebido.
/// </summary>
public sealed class WebhookDispatcher(
    IServiceScopeFactory scopes,
    IHttpClientFactory httpFactory,
    TimeProvider clock,
    IOptions<WebhookOptions> options,
    ILogger<WebhookDispatcher> logger)
{
    public const string HttpClientName = "webhooks";
    private const int Batch = 50;
    /// <summary>Reserva da entrega enquanto ela está em andamento: se o processo cair no meio, outra rodada pega.</summary>
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    public async Task DeliverDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        List<long> ids;
        Dictionary<long, Guid> endpointOf;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Um UPDATE só: pega e reserva o lote (outra instância da app pula as linhas travadas).
            ids = await db.Database.SqlQuery<long>($"""
                UPDATE webhook_deliveries SET next_attempt_at = {now.Add(Lease)}
                WHERE id IN (
                    SELECT id FROM webhook_deliveries
                    WHERE status = 'pending' AND next_attempt_at <= {now}
                    ORDER BY next_attempt_at, id LIMIT {Batch}
                    FOR UPDATE SKIP LOCKED)
                RETURNING id AS "Value"
                """).ToListAsync(ct);
            endpointOf = ids.Count == 0 ? [] : await db.WebhookDeliveries.Where(d => ids.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.EndpointId, ct);
        }
        if (ids.Count == 0)
            return;

        // Endpoints em paralelo; dentro de cada um, na ordem em que os eventos aconteceram.
        var perEndpoint = ids.Where(endpointOf.ContainsKey).GroupBy(id => endpointOf[id]).Select(g => g.Order().ToList());
        await Parallel.ForEachAsync(perEndpoint, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.Value.Concurrency),
            CancellationToken = ct,
        }, async (batch, token) =>
        {
            foreach (var id in batch)
                await DeliverAsync(id, token);
        });
    }

    private async ValueTask DeliverAsync(long id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.WebhookDeliveries.SingleOrDefaultAsync(d => d.Id == id, ct);
        if (delivery is null)
            return;
        var ev = await db.WebhookEvents.AsNoTracking().SingleAsync(e => e.Id == delivery.EventId, ct);
        var endpoint = await db.WebhookEndpoints.AsNoTracking().SingleOrDefaultAsync(e => e.Id == delivery.EndpointId, ct);

        var now = clock.GetUtcNow();
        if (endpoint is null || !endpoint.Enabled)
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
            delivery.LastError = "Endpoint desativado antes da entrega.";
            await db.SaveChangesAsync(ct);
            return;
        }

        delivery.Attempts++;
        delivery.LastAttemptAt = now;
        var (ok, status, error, body) = await PostAsync(scope.ServiceProvider, endpoint, ev, delivery, ct);
        delivery.LastStatusCode = status;
        delivery.LastError = error is { Length: > 500 } ? error[..500] : error;
        delivery.ResponseBody = body;

        var delays = options.Value.Delays;
        if (ok)
        {
            delivery.Status = WebhookDeliveryStatus.Succeeded;
            delivery.DeliveredAt = now;
            await db.WebhookEndpoints.Where(e => e.Id == endpoint.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(e => e.LastSuccessAt, now).SetProperty(e => e.FailureStreak, 0), ct);
        }
        else if (delivery.Attempts > delays.Length)
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
            // Primeira entrega perdida desde a última que deu certo: avisa os Admins (uma vez por sequência de falhas).
            var streak = await db.Database.SqlQuery<int>($"""
                UPDATE webhook_endpoints SET failure_streak = failure_streak + 1, last_failure_at = {now}
                WHERE id = {endpoint.Id} RETURNING failure_streak AS "Value"
                """).ToListAsync(ct);
            if (streak is [1])
                db.Enqueue(OutboxTypes.WebhookFailingAlert, new WebhookFailingMessage(endpoint.OrganizationId, endpoint.Id, delivery.LastError ?? "sem resposta"));
            logger.LogWarning("Webhook {DeliveryId} para {EndpointId} desistiu depois de {Attempts} tentativas", delivery.Id, endpoint.Id, delivery.Attempts);
        }
        else
        {
            delivery.NextAttemptAt = now.AddSeconds(delays[delivery.Attempts - 1]);
            await db.WebhookEndpoints.Where(e => e.Id == endpoint.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(e => e.LastFailureAt, now), ct);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<(bool Ok, int? Status, string? Error, string? Body)> PostAsync(
        IServiceProvider services, WebhookEndpoint endpoint, WebhookEvent ev, WebhookDelivery delivery, CancellationToken ct)
    {
        string secret;
        try
        {
            secret = services.GetRequiredService<WebhookSecrets>().Unprotect(endpoint.SecretProtected);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // As chaves do Data Protection se perderam (volume apagado): só gerando um segredo novo.
            return (false, null, "Não foi possível ler o segredo do endpoint. Gere um novo segredo em Integrações.", null);
        }

        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
        {
            Content = new StringContent(ev.Payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(WebhookSignature.Header, WebhookSignature.Compute(secret, timestamp, ev.Payload));
        request.Headers.TryAddWithoutValidation("X-Contratos-Event", ev.Type);
        request.Headers.TryAddWithoutValidation("X-Contratos-Event-Id", ev.Id.ToString());
        request.Headers.TryAddWithoutValidation("X-Contratos-Delivery", delivery.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-Contratos-Attempt", delivery.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.UserAgent.ParseAdd("Contratos-Webhooks/1.0");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds)));
            using var response = await httpFactory.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var body = await ReadStartAsync(response, timeout.Token);
            var code = (int)response.StatusCode;
            return response.IsSuccessStatusCode
                ? (true, code, null, body)
                : (false, code, code is >= 300 and < 400
                    ? $"HTTP {code}: redirecionamentos não são seguidos. Cadastre a URL final."
                    : $"HTTP {code}", body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, null, $"Sem resposta em {options.Value.TimeoutSeconds} s.", null);
        }
        catch (HttpRequestException e)
        {
            var message = e.InnerException is { } inner && !e.Message.Contains(inner.Message, StringComparison.Ordinal)
                ? $"{e.Message} ({inner.Message})"
                : e.Message;
            return (false, null, message, null);
        }
    }

    private static async Task<string?> ReadStartAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[1000];
            var read = 0;
            int n;
            while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read), ct)) > 0)
                read += n;
            return read == 0 ? null : Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            return null;
        }
    }
}

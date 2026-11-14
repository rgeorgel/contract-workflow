using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Contratos.Application.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Email;

public sealed class ResendOptions
{
    public const string Section = "Resend";

    /// <summary>Chave da API (re_...). Basta permissão "Sending access".</summary>
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://api.resend.com/";
    /// <summary>Espera entre tentativas quando a Resend devolve 429/5xx sem Retry-After.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}

public sealed class EmailDeliveryException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Envia pela API HTTP da Resend (POST /emails).</summary>
internal sealed class ResendEmailSender(
    HttpClient http,
    IOptions<EmailOptions> emailOptions,
    IOptions<ResendOptions> resendOptions,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    internal const int MaxAttempts = 3;
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(5);

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var from = emailOptions.Value;
        var payload = new ResendEmail(
            FormatMailbox(from.FromName, from.FromAddress),
            [message.To],
            message.Subject,
            message.HtmlBody,
            message.TextBody);

        // A mesma chave em todas as tentativas: se a primeira chegou e só a resposta se perdeu,
        // a Resend não manda o e-mail de novo.
        var idempotencyKey = Guid.NewGuid().ToString();

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
            {
                Content = JsonContent.Create(payload, options: Json),
            };
            request.Headers.Add("Idempotency-Key", idempotencyKey);

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, ct);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning(ex, "Falha de rede ao enviar e-mail pela Resend (tentativa {Attempt})", attempt);
                await Task.Delay(resendOptions.Value.RetryDelay, ct);
                continue;
            }
            catch (HttpRequestException ex)
            {
                throw new EmailDeliveryException("Não foi possível falar com a Resend.", ex);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    // LGPD: nem destinatário nem assunto vão para o log; o id permite achar o envio no painel da Resend.
                    var sent = await ReadAsync<ResendSent>(response, ct);
                    logger.LogInformation("E-mail do sistema enviado pela Resend ({ResendId})", sent?.Id);
                    return;
                }

                if (attempt < MaxAttempts && IsTransient(response.StatusCode))
                {
                    logger.LogWarning("Resend respondeu {Status}; nova tentativa ({Attempt})", (int)response.StatusCode, attempt);
                    await Task.Delay(RetryDelay(response), ct);
                    continue;
                }

                // Só o código e o nome do erro: a mensagem da Resend pode citar o endereço do destinatário.
                var error = await ReadAsync<ResendError>(response, ct);
                throw new EmailDeliveryException(
                    $"A Resend recusou o e-mail: {(int)response.StatusCode} {error?.Name ?? response.ReasonPhrase}.");
            }
        }
    }

    internal static string FormatMailbox(string? name, string address)
    {
        if (string.IsNullOrWhiteSpace(name))
            return address;
        var quoted = name.Trim().Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{quoted}\" <{address}>";
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private TimeSpan RetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } date)
            retryAfter = date - DateTimeOffset.UtcNow;
        var delay = retryAfter ?? resendOptions.Value.RetryDelay;
        if (delay < TimeSpan.Zero)
            return TimeSpan.Zero;
        return delay > MaxRetryAfter ? MaxRetryAfter : delay;
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct) where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record ResendEmail(string From, string[] To, string Subject, string Html, string Text);
    private sealed record ResendSent(string? Id);
    private sealed record ResendError(string? Name, string? Message);
}

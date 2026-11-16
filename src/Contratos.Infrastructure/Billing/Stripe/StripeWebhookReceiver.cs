using System.Text.Json;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace Contratos.Infrastructure.Billing.Stripe;

/// <summary>
/// Recebe webhooks do Stripe: confere a assinatura (Stripe-Signature, com tolerância de 5 minutos contra
/// reenvio) e guarda o evento na webhook_inbox. O worker processa depois, buscando o estado atual na API.
/// </summary>
public sealed class StripeWebhookReceiver(AppDbContext db, IOptions<StripeOptions> options, ILogger<StripeWebhookReceiver> logger)
{
    public const string ProviderName = "stripe";
    public enum Outcome { Accepted, Ignored, InvalidSignature, Malformed }

    public async Task<Outcome> ReceiveAsync(string rawBody, string? signatureHeader, CancellationToken ct)
    {
        var secret = options.Value.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signatureHeader))
            return Outcome.InvalidSignature;
        try
        {
            EventUtility.ValidateSignature(rawBody, signatureHeader, secret.Trim(), 300);
        }
        catch (StripeException)
        {
            return Outcome.InvalidSignature;
        }

        string? id, type;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            id = doc.RootElement.GetProperty("id").GetString();
            type = doc.RootElement.GetProperty("type").GetString();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(e, "Webhook do Stripe malformado");
            return Outcome.Malformed;
        }
        if (string.IsNullOrEmpty(id) || type is null)
            return Outcome.Malformed;
        if (!StripeSubscriptionSync.HandledEvents.Contains(type))
            return Outcome.Ignored;

        // Idempotência: o mesmo evento reentregue é ignorado pelo índice único.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO webhook_inbox (provider, external_event_id, payload, received_at, attempts)
            VALUES ({ProviderName}, {id}, {rawBody}::jsonb, now(), 0)
            ON CONFLICT (provider, external_event_id) DO NOTHING
            """, ct);
        return Outcome.Accepted;
    }
}

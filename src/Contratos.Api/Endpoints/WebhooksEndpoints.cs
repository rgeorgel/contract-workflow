using Contratos.Api.Security;
using Contratos.Application.Common;
using Contratos.Application.Signatures;
using Contratos.Domain.Contracts;
using Contratos.Infrastructure.Billing.Stripe;
using Contratos.Infrastructure.Messaging;
using Contratos.Infrastructure.Persistence;
using Contratos.Infrastructure.Signatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Contratos.Api.Endpoints;

internal static class WebhooksEndpoints
{
    public const string SignatureHeader = "x-autentique-signature";

    /// <summary>Fora do grupo /api/v1 com antiforgery: quem chama é o provedor, sem cookie.</summary>
    public static void MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/webhooks/autentique", async (HttpRequest request, WebhookReceiver receiver, ILoggerFactory loggers, CancellationToken ct) =>
            {
                using var reader = new StreamReader(request.Body);
                var raw = await reader.ReadToEndAsync(ct); // corpo bruto: é sobre ele que o HMAC é calculado
                var signature = request.Headers[SignatureHeader].ToString();
                var outcome = await receiver.ReceiveAsync(raw, signature, ct);
                if (outcome == WebhookReceiver.Outcome.InvalidSignature)
                    LogRejected(loggers.CreateLogger("Contratos.Webhooks"), request, signature);
                return outcome switch
                {
                    WebhookReceiver.Outcome.InvalidSignature => Results.Unauthorized(),
                    WebhookReceiver.Outcome.Malformed => Results.BadRequest(),
                    _ => Results.Ok(),
                };
            })
            .AllowAnonymous()
            .WithTags("Webhooks")
            .WithSummary("Eventos do provedor de assinatura (valida HMAC-SHA256)");

        // Só existe com o Stripe configurado.
        if (app.ServiceProvider.GetService<IOptions<StripeOptions>>()?.Value.Enabled == true)
            app.MapPost("/api/v1/webhooks/stripe", async (HttpRequest request, StripeWebhookReceiver receiver, ILoggerFactory loggers, CancellationToken ct) =>
                {
                    using var reader = new StreamReader(request.Body);
                    var raw = await reader.ReadToEndAsync(ct);
                    var outcome = await receiver.ReceiveAsync(raw, request.Headers["Stripe-Signature"].ToString(), ct);
                    if (outcome == StripeWebhookReceiver.Outcome.InvalidSignature)
                        loggers.CreateLogger("Contratos.Webhooks").LogWarning(
                            "Webhook do Stripe recusado: Stripe-Signature ausente, expirada ou que não confere com Stripe__WebhookSecret");
                    return outcome switch
                    {
                        StripeWebhookReceiver.Outcome.InvalidSignature => Results.BadRequest(),
                        StripeWebhookReceiver.Outcome.Malformed => Results.BadRequest(),
                        _ => Results.Ok(),
                    };
                })
                .AllowAnonymous()
                .WithTags("Webhooks")
                .WithSummary("Eventos de cobrança do Stripe (valida Stripe-Signature)");
    }

    /// <summary>
    /// Diz por que o webhook foi recusado, sem valores: se o cabeçalho de assinatura nem veio (o provedor não
    /// assina, e a lista de nomes de cabeçalho mostra o que veio no lugar) ou se veio e não bate com o secret.
    /// </summary>
    private static void LogRejected(ILogger logger, HttpRequest request, string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
            logger.LogWarning(
                "Webhook recusado: sem o cabeçalho {Header}. Cabeçalhos recebidos: {Headers}",
                SignatureHeader, string.Join(", ", request.Headers.Keys.Order(StringComparer.OrdinalIgnoreCase)));
        else
            logger.LogWarning(
                "Webhook recusado: o cabeçalho {Header} veio ({Length} caracteres) mas não confere com nenhum secret configurado em Autentique__WebhookSecret",
                SignatureHeader, signature.Length);
    }

    /// <summary>
    /// Simulador do provedor fake: gera o webhook que a Autentique mandaria (assinado), passa pelo mesmo
    /// endpoint lógico e processa na hora. Só existe com Signature__Provider=Fake.
    /// </summary>
    public static RouteGroupBuilder MapFakeProviderEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/dev/fake-provider/contracts/{id:guid}/signers/{signerId:guid}/{action}", async (
                Guid id, Guid signerId, string action, AppDbContext db, ICurrentTenant tenant,
                FakeSignatureProvider fake, WebhookReceiver receiver, BackgroundProcessor processor, CancellationToken ct) =>
            {
                if (!tenant.CanEditContracts())
                    return ResultHttp.Problem(StatusCodes.Status403Forbidden, "Seu perfil não permite simular.", "forbidden");
                var contract = await db.Contracts.Include(c => c.Signers).SingleOrDefaultAsync(c => c.Id == id, ct);
                var signer = contract?.Signers.SingleOrDefault(s => s.Id == signerId);
                if (contract?.AutentiqueDocumentId is null || signer?.AutentiquePublicId is null)
                    return ResultHttp.Problem(StatusCodes.Status404NotFound, "Contrato ainda não chegou ao provedor.", "not_sent");

                SignatureEventType? type = action switch
                {
                    "view" => SignatureEventType.Viewed,
                    "sign" => SignatureEventType.Signed,
                    "decline" => SignatureEventType.Declined,
                    "fail" => SignatureEventType.DeliveryFailed,
                    _ => null,
                };
                if (type is null)
                    return ResultHttp.Problem(StatusCodes.Status400BadRequest, "Ação inválida.", "invalid_action");

                var now = DateTimeOffset.UtcNow;
                var events = new List<SignatureEvent>
                {
                    new($"fake-{Guid.NewGuid():N}", type.Value, contract.AutentiqueDocumentId, signer.AutentiquePublicId,
                        type == SignatureEventType.Declined ? "Recusa simulada" : null, now),
                };
                // Última assinatura: o provedor também avisa que o documento terminou.
                if (type == SignatureEventType.Signed && contract.Signers.All(s => s.Id == signerId || s.Status == SignerStatus.Signed))
                    events.Add(new($"fake-{Guid.NewGuid():N}", SignatureEventType.DocumentFinished, contract.AutentiqueDocumentId, null, null, now.AddSeconds(1)));

                foreach (var ev in events)
                {
                    var (body, signature) = fake.BuildWebhook(ev);
                    await receiver.ReceiveAsync(body, signature, ct);
                }
                await processor.ProcessAllAsync(ct);
                return Results.NoContent();
            })
            .RequireAuthorization(Policies.CanEditContracts)
            .WithTags("Desenvolvimento")
            .WithSummary("Simula um evento do signatário no provedor fake");
        return api;
    }
}

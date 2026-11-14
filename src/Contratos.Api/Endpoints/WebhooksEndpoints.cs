using Contratos.Api.Security;
using Contratos.Application.Common;
using Contratos.Application.Signatures;
using Contratos.Domain.Contracts;
using Contratos.Infrastructure.Messaging;
using Contratos.Infrastructure.Persistence;
using Contratos.Infrastructure.Signatures;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Api.Endpoints;

internal static class WebhooksEndpoints
{
    public const string SignatureHeader = "x-autentique-signature";

    /// <summary>Fora do grupo /api/v1 com antiforgery: quem chama é o provedor, sem cookie.</summary>
    public static void MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/webhooks/autentique", async (HttpRequest request, WebhookReceiver receiver, CancellationToken ct) =>
            {
                using var reader = new StreamReader(request.Body);
                var raw = await reader.ReadToEndAsync(ct); // corpo bruto: é sobre ele que o HMAC é calculado
                return await receiver.ReceiveAsync(raw, request.Headers[SignatureHeader].ToString(), ct) switch
                {
                    WebhookReceiver.Outcome.InvalidSignature => Results.Unauthorized(),
                    WebhookReceiver.Outcome.Malformed => Results.BadRequest(),
                    _ => Results.Ok(),
                };
            })
            .AllowAnonymous()
            .WithTags("Webhooks")
            .WithSummary("Eventos do provedor de assinatura (valida HMAC-SHA256)");
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

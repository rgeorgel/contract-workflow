using Contratos.Api.Security;
using Contratos.Application.Integrations;

namespace Contratos.Api.Endpoints;

/// <summary>Tela de Integrações: chaves de API, webhooks e entregas. Só Admin.</summary>
internal static class IntegrationsEndpoints
{
    public static RouteGroupBuilder MapIntegrationsEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/integrations").WithTags("Integrações").RequireAuthorization(Policies.IsAdmin);

        g.MapGet("/", async (IIntegrationService s, CancellationToken ct) => (await s.GetAsync(ct)).ToHttp())
            .WithSummary("Chaves, webhooks e tipos de evento");

        g.MapPost("/keys", async (CreateApiKeyRequest req, IIntegrationService s, CancellationToken ct) =>
                (await s.CreateKeyAsync(req, ct)).ToHttp(k => TypedResults.Created($"/api/v1/integrations/keys/{k.Key.Id}", k)))
            .WithSummary("Cria uma chave; a chave completa só vem nesta resposta");
        g.MapDelete("/keys/{id:guid}", async (Guid id, IIntegrationService s, CancellationToken ct) => (await s.RevokeKeyAsync(id, ct)).ToHttp())
            .WithSummary("Revoga a chave (para de funcionar na hora)");

        g.MapPost("/webhooks", async (WebhookEndpointRequest req, IIntegrationService s, CancellationToken ct) =>
                (await s.CreateEndpointAsync(req, ct)).ToHttp(w => TypedResults.Created($"/api/v1/integrations/webhooks/{w.Endpoint.Id}", w)))
            .WithSummary("Cadastra um endereço; o segredo de assinatura vem na resposta");
        g.MapPut("/webhooks/{id:guid}", async (Guid id, WebhookEndpointRequest req, IIntegrationService s, CancellationToken ct) =>
            (await s.UpdateEndpointAsync(id, req, ct)).ToHttp());
        g.MapDelete("/webhooks/{id:guid}", async (Guid id, IIntegrationService s, CancellationToken ct) =>
            (await s.DeleteEndpointAsync(id, ct)).ToHttp());
        g.MapGet("/webhooks/{id:guid}/secret", async (Guid id, IIntegrationService s, CancellationToken ct) =>
            (await s.GetSecretAsync(id, ct)).ToHttp());
        g.MapPost("/webhooks/{id:guid}/secret", async (Guid id, IIntegrationService s, CancellationToken ct) =>
                (await s.RotateSecretAsync(id, ct)).ToHttp())
            .WithSummary("Gera um segredo novo (o antigo para de valer na hora)");
        g.MapPost("/webhooks/{id:guid}/test", async (Guid id, IIntegrationService s, CancellationToken ct) =>
            (await s.SendTestAsync(id, ct)).ToHttp());

        g.MapGet("/deliveries", async (Guid? endpointId, int? page, IIntegrationService s, CancellationToken ct) =>
            (await s.ListDeliveriesAsync(endpointId, page ?? 1, ct)).ToHttp());
        g.MapPost("/deliveries/{id:long}/redeliver", async (long id, IIntegrationService s, CancellationToken ct) =>
            (await s.RedeliverAsync(id, ct)).ToHttp());

        g.MapPost("/embed-origins", async (EmbedOriginRequest req, IIntegrationService s, CancellationToken ct) =>
                (await s.AddEmbedOriginAsync(req, ct)).ToHttp(o => TypedResults.Created($"/api/v1/integrations/embed-origins/{o.Id}", o)))
            .WithSummary("Cadastra um site que pode mostrar o embed de templates");
        g.MapDelete("/embed-origins/{id:guid}", async (Guid id, IIntegrationService s, CancellationToken ct) =>
            (await s.DeleteEmbedOriginAsync(id, ct)).ToHttp());

        return api;
    }
}

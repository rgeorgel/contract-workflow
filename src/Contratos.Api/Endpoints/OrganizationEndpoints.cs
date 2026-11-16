using Contratos.Api.Security;
using Contratos.Application.Billing;
using Contratos.Application.Organizations;

namespace Contratos.Api.Endpoints;

internal static class OrganizationEndpoints
{
    public static RouteGroupBuilder MapOrganizationEndpoints(this RouteGroupBuilder api)
    {
        // Público: a página inicial mostra os preços a partir do mesmo catálogo que aplica os limites.
        api.MapGet("/plans", (HttpContext http) =>
            {
                http.Response.Headers.CacheControl = "public, max-age=300";
                return TypedResults.Ok(PlanCatalogDto.Current);
            })
            .AllowAnonymous()
            .WithTags("Planos")
            .WithSummary("Planos e pacotes de contratos avulsos, com preços");

        var g = api.MapGroup("/organization").WithTags("Organização").RequireAuthorization();

        g.MapGet("/settings", async (IOrganizationService orgs, CancellationToken ct) => TypedResults.Ok(await orgs.GetAsync(ct)))
            .WithSummary("Dados da organização, padrões de envio e uso do mês");

        g.MapPut("/settings", async (UpdateOrganizationRequest req, IOrganizationService orgs, CancellationToken ct) =>
                (await orgs.UpdateAsync(req, ct)).ToHttp())
            .RequireAuthorization(Policies.IsAdmin)
            .WithSummary("Atualiza nome e padrões de envio (Admin)");

        g.MapGet("/billing", async (IBillingService billing, CancellationToken ct) => TypedResults.Ok(await billing.GetAsync(ct)))
            .WithSummary("Plano, uso do ciclo, créditos avulsos e catálogo de planos");

        var billing = api.MapGroup("/billing").WithTags("Assinatura").RequireAuthorization(Policies.IsAdmin);
        billing.MapPost("/checkout", async (CheckoutRequest req, ISubscriptionService subs, CancellationToken ct) =>
                (await subs.CheckoutAsync(req, ct)).ToHttp())
            .WithSummary("Abre o pagamento (Stripe Checkout, só cartão) para assinar um plano (Admin)");
        billing.MapPost("/checkout/confirm", async (ConfirmCheckoutRequest req, ISubscriptionService subs, CancellationToken ct) =>
                (await subs.ConfirmCheckoutAsync(req, ct)).ToHttp())
            .WithSummary("Confere a sessão de pagamento na volta e ativa o plano (Admin)");
        billing.MapPost("/portal", async (ISubscriptionService subs, CancellationToken ct) =>
                (await subs.PortalAsync(ct)).ToHttp())
            .WithSummary("Abre o portal do Stripe: trocar plano, cartão, cancelar, faturas (Admin)");
        billing.MapPost("/packs/checkout", async (BuyPackRequest req, ISubscriptionService subs, CancellationToken ct) =>
                (await subs.BuyPackAsync(req, ct)).ToHttp())
            .WithSummary("Abre o pagamento (só cartão) de um pacote de contratos avulsos (Admin)");
        billing.MapPost("/packs/confirm", async (ConfirmCheckoutRequest req, ISubscriptionService subs, CancellationToken ct) =>
                (await subs.ConfirmPackAsync(req, ct)).ToHttp())
            .WithSummary("Confere o pagamento do pacote na volta e libera os créditos (Admin)");

        return api;
    }
}

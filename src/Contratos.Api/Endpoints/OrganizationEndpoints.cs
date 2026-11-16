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

        return api;
    }
}

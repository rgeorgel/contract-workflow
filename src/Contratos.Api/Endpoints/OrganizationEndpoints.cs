using Contratos.Api.Security;
using Contratos.Application.Organizations;

namespace Contratos.Api.Endpoints;

internal static class OrganizationEndpoints
{
    public static RouteGroupBuilder MapOrganizationEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/organization").WithTags("Organização").RequireAuthorization();

        g.MapGet("/settings", async (IOrganizationService orgs, CancellationToken ct) => TypedResults.Ok(await orgs.GetAsync(ct)))
            .WithSummary("Dados da organização, padrões de envio e uso do mês");

        g.MapPut("/settings", async (UpdateOrganizationRequest req, IOrganizationService orgs, CancellationToken ct) =>
                (await orgs.UpdateAsync(req, ct)).ToHttp())
            .RequireAuthorization(Policies.IsAdmin)
            .WithSummary("Atualiza nome e padrões de envio (Admin)");

        return api;
    }
}

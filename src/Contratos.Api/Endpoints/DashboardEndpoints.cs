using Contratos.Application.Dashboard;

namespace Contratos.Api.Endpoints;

internal static class DashboardEndpoints
{
    public static RouteGroupBuilder MapDashboardEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard/summary", async (int? staleDays, IDashboardService dashboard, CancellationToken ct) =>
                TypedResults.Ok(await dashboard.GetSummaryAsync(staleDays ?? 3, ct)))
            .RequireAuthorization()
            .WithTags("Painel")
            .WithSummary("Contadores por status, pendentes antigos e concluídos recentes");

        api.MapGet("/organization/members", async (IDashboardService dashboard, CancellationToken ct) =>
                TypedResults.Ok(await dashboard.MembersAsync(ct)))
            .RequireAuthorization()
            .WithTags("Organização")
            .WithSummary("Pessoas da organização (para filtros como \"criado por\")");
        return api;
    }
}

using Contratos.Api.Security;
using Contratos.Application.Billing;
using Contratos.Application.Platform;

namespace Contratos.Api.Endpoints;

/// <summary>
/// Portal super admin (<c>/superadmin/</c>): a equipe da plataforma acompanha e gerencia todas as organizações.
/// Entra pelo login normal; o acesso vem de <c>Platform:AdminEmails</c> (claim <c>platform_admin</c>).
/// Fica no grupo com antiforgery, como o resto da tela.
/// </summary>
internal static class SuperAdminEndpoints
{
    public static RouteGroupBuilder MapSuperAdminEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/superadmin").WithTags("Super admin").RequireAuthorization(Policies.PlatformAdmin)
            .ExcludeFromDescription();

        g.MapGet("/overview", async (ISuperAdminService svc, CancellationToken ct) => TypedResults.Ok(await svc.OverviewAsync(ct)))
            .WithSummary("Números da plataforma e contas que pedem atenção");

        g.MapGet("/tenants", async (string? q, string? plan, string? status, string? activity, string? sort, int? page, int? pageSize,
                ISuperAdminService svc, CancellationToken ct) =>
                TypedResults.Ok(await svc.ListAsync(new TenantListQuery(q, plan, status, activity, sort, page ?? 1, pageSize ?? 24), ct)))
            .WithSummary("Organizações com plano, uso e atividade");

        g.MapGet("/tenants/{id:guid}", async (Guid id, ISuperAdminService svc, CancellationToken ct) =>
                (await svc.GetAsync(id, ct)).ToHttp())
            .WithSummary("Detalhe de uma organização");

        g.MapGet("/tenants/{id:guid}/timeline", async (Guid id, string? category, int? days, ISuperAdminService svc, CancellationToken ct) =>
                (await svc.TimelineAsync(id, string.IsNullOrWhiteSpace(category) ? null : category, days ?? 30, ct)).ToHttp())
            .WithSummary("Linha do tempo agrupada por dia e tipo de evento");

        g.MapPut("/tenants/{id:guid}/plan", async (Guid id, ChangePlanRequest req, ISuperAdminService svc, CancellationToken ct) =>
                (await svc.ChangePlanAsync(id, req, ct)).ToHttp())
            .WithSummary("Troca o plano (contas sem assinatura no Stripe)");

        g.MapPost("/tenants/{id:guid}/credits", async (Guid id, GrantCreditsRequest req, ISuperAdminService svc, CancellationToken ct) =>
                (await svc.GrantCreditsAsync(id, req, ct)).ToHttp())
            .WithSummary("Concede contratos avulsos");

        g.MapPost("/tenants/{id:guid}/suspend", async (Guid id, SuspendRequest req, ISuperAdminService svc, CancellationToken ct) =>
                (await svc.SuspendAsync(id, req, ct)).ToHttp())
            .WithSummary("Suspende o acesso da organização (tela, API e embed)");

        g.MapPost("/tenants/{id:guid}/reactivate", async (Guid id, ISuperAdminService svc, CancellationToken ct) =>
                (await svc.ReactivateAsync(id, ct)).ToHttp())
            .WithSummary("Reativa uma organização suspensa");

        g.MapPost("/tenants/{id:guid}/users/{userId:guid}/impersonate",
                async (Guid id, Guid userId, Contratos.Infrastructure.Platform.ImpersonationService svc, CancellationToken ct) =>
                (await svc.StartAsync(id, userId, ct)).ToHttp())
            .WithSummary("Entrar como: abre uma sessão de 1 hora na conta do usuário, com acesso total e auditada");

        return api;
    }
}

using Contratos.Api.Security;
using Contratos.Application.Billing;
using Contratos.Application.Platform;
using Contratos.Infrastructure.Platform;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace Contratos.Api.Endpoints;

public sealed record AdminLoginRequest(string? Email, string? Password);
public sealed record AdminMeResponse(Guid Id, string Name, string Email);

/// <summary>
/// API do portal super admin (<c>/superadmin/</c>), separada da do app: prefixo próprio, login próprio
/// (contas em <c>platform_admins</c>) e cookie próprio. As rotas usam só o esquema <see cref="PlatformAdminAuth.Scheme"/>:
/// o cookie dos clientes não conta aqui. Antiforgery em toda escrita, como no app.
/// </summary>
internal static class SuperAdminEndpoints
{
    public const string ApiPrefix = "/superadmin/api";

    public static void MapSuperAdminPortal(this WebApplication app)
    {
        var api = app.MapGroup(ApiPrefix + "/v1").WithTags("Super admin").ExcludeFromDescription()
            .RequireAuthorization(Policies.PlatformAdmin)
            .AddEndpointFilter<AntiforgeryFilter>();

        // ---------- Sessão ----------
        var auth = api.MapGroup("/auth");

        // Anônimo, mas avaliado com o esquema do portal: o token antiforgery fica ligado à conta (ou a ninguém).
        auth.MapGet("/csrf", (HttpContext ctx, IAntiforgery af) =>
                TypedResults.Ok(new CsrfResponse(af.GetAndStoreTokens(ctx).RequestToken!)))
            .AllowAnonymous();

        auth.MapPost("/login", async (AdminLoginRequest req, PlatformAdminService admins, HttpContext http, CancellationToken ct) =>
            {
                var result = await admins.LoginAsync(req.Email, req.Password, ct);
                switch (result.Outcome)
                {
                    case AdminLoginOutcome.LockedOut:
                        return ResultHttp.Problem(StatusCodes.Status423Locked,
                            "Muitas tentativas. Tente de novo em alguns minutos.", "locked_out");
                    case AdminLoginOutcome.Invalid:
                        return ResultHttp.Problem(StatusCodes.Status401Unauthorized, "E-mail ou senha incorretos.", "invalid_credentials");
                }
                await http.SignInAsync(PlatformAdminAuth.Scheme, PlatformAdminAuth.PrincipalFor(result.Admin!),
                    new AuthenticationProperties { IsPersistent = false });
                return TypedResults.NoContent();
            })
            .AllowAnonymous()
            .RequireRateLimiting(AuthEndpoints.RateLimitPolicy);

        auth.MapPost("/logout", async (HttpContext http) =>
            {
                await http.SignOutAsync(PlatformAdminAuth.Scheme);
                return TypedResults.NoContent();
            })
            .AllowAnonymous();

        auth.MapGet("/me", (ICurrentPlatformAdmin me) => TypedResults.Ok(new AdminMeResponse(me.Id!.Value, me.Name!, me.Email!)));

        // ---------- Organizações ----------

        api.MapGet("/overview", async (ISuperAdminService svc, CancellationToken ct) => TypedResults.Ok(await svc.OverviewAsync(ct)));

        api.MapGet("/tenants", async (string? q, string? plan, string? status, string? activity, string? sort, int? page, int? pageSize,
                ISuperAdminService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(new TenantListQuery(q, plan, status, activity, sort, page ?? 1, pageSize ?? 24), ct)));

        api.MapGet("/tenants/{id:guid}", async (Guid id, ISuperAdminService svc, CancellationToken ct) =>
            (await svc.GetAsync(id, ct)).ToHttp());

        api.MapGet("/tenants/{id:guid}/timeline", async (Guid id, string? category, int? days, ISuperAdminService svc, CancellationToken ct) =>
            (await svc.TimelineAsync(id, string.IsNullOrWhiteSpace(category) ? null : category, days ?? 30, ct)).ToHttp());

        api.MapPut("/tenants/{id:guid}/plan", async (Guid id, ChangePlanRequest req, ISuperAdminService svc, CancellationToken ct) =>
            (await svc.ChangePlanAsync(id, req, ct)).ToHttp());

        api.MapPost("/tenants/{id:guid}/credits", async (Guid id, GrantCreditsRequest req, ISuperAdminService svc, CancellationToken ct) =>
            (await svc.GrantCreditsAsync(id, req, ct)).ToHttp());

        api.MapPost("/tenants/{id:guid}/suspend", async (Guid id, SuspendRequest req, ISuperAdminService svc, CancellationToken ct) =>
            (await svc.SuspendAsync(id, req, ct)).ToHttp());

        api.MapPost("/tenants/{id:guid}/reactivate", async (Guid id, ISuperAdminService svc, CancellationToken ct) =>
            (await svc.ReactivateAsync(id, ct)).ToHttp());

        // "Entrar como": abre uma sessão de 1 hora no app como o usuário (acesso total, auditada).
        api.MapPost("/tenants/{id:guid}/users/{userId:guid}/impersonate",
            async (Guid id, Guid userId, ImpersonationService svc, CancellationToken ct) => (await svc.StartAsync(id, userId, ct)).ToHttp());
    }
}

using System.Security.Claims;
using Contratos.Application.Platform;
using Contratos.Infrastructure.Platform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Contratos.Api.Security;

/// <summary>
/// Autenticação do portal super admin, separada da dos clientes: outro esquema, outro cookie
/// (<c>contratos.admin</c>, só enviado em <c>/superadmin</c>) e outros claims. O cookie dos clientes não
/// abre o portal, e o do portal não abre o app.
/// </summary>
internal static class PlatformAdminAuth
{
    public const string Scheme = "PlatformAdmin";
    public const string CookiePath = "/superadmin";

    // Claims próprios (sem NameIdentifier): nada do app dos clientes enxerga uma conta do portal como usuário.
    public const string IdClaim = "pa_id";
    public const string NameClaim = "pa_name";
    public const string EmailClaim = "pa_email";
    public const string StampClaim = "pa_stamp";

    public static AuthenticationBuilder AddPlatformAdminCookie(this AuthenticationBuilder auth, CookieSecurePolicy secure) =>
        auth.AddCookie(Scheme, o =>
        {
            o.Cookie.Name = "contratos.admin";
            o.Cookie.Path = CookiePath;
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = secure;
            o.ExpireTimeSpan = TimeSpan.FromHours(4);
            o.SlidingExpiration = true;
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            // Toda requisição confere a conta: senha trocada ou conta desativada derrubam a sessão na hora.
            o.Events.OnValidatePrincipal = async ctx =>
            {
                var principal = ctx.Principal;
                var valid = Guid.TryParse(principal?.FindFirstValue(IdClaim), out var id)
                    && await ctx.HttpContext.RequestServices.GetRequiredService<PlatformAdminService>()
                        .IsSessionValidAsync(id, principal!.FindFirstValue(StampClaim), ctx.HttpContext.RequestAborted);
                if (!valid)
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(Scheme);
                }
            };
        });

    public static ClaimsPrincipal PrincipalFor(PlatformAdmin admin) => new(new ClaimsIdentity(
    [
        new Claim(IdClaim, admin.Id.ToString()),
        new Claim(NameClaim, admin.Name),
        new Claim(EmailClaim, admin.Email),
        new Claim(StampClaim, admin.SecurityStamp),
    ], Scheme, NameClaim, null));
}

/// <summary>Conta do portal na requisição (só nas rotas do portal, onde o usuário vem do esquema dele).</summary>
internal sealed class HttpCurrentPlatformAdmin(IHttpContextAccessor accessor) : ICurrentPlatformAdmin
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public Guid? Id => Guid.TryParse(User?.FindFirstValue(PlatformAdminAuth.IdClaim), out var id) ? id : null;
    public string? Name => Id is null ? null : User?.FindFirstValue(PlatformAdminAuth.NameClaim);
    public string? Email => Id is null ? null : User?.FindFirstValue(PlatformAdminAuth.EmailClaim);
}

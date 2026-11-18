using System.Security.Claims;
using Contratos.Infrastructure.Identity;
using Contratos.Infrastructure.Persistence;
using Contratos.Infrastructure.Platform;

namespace Contratos.Api.Security;

/// <summary>
/// Durante o "Entrar como", registra na auditoria da plataforma cada escrita (POST/PUT/PATCH/DELETE)
/// com o método, a rota (sem query string) e o status, em nome de quem da equipe está por trás.
/// Grava num escopo próprio: o DbContext da requisição pode ter mudanças que ela decidiu não salvar.
/// </summary>
internal sealed class ImpersonationAuditFilter(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ImpersonationAuditFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var method = http.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)
            || !ImpersonationService.IsImpersonating(http.User)
            // Entrar e sair já ficam registrados pelo próprio fluxo.
            || http.Request.Path.StartsWithSegments("/api/v1/auth"))
            return await next(context);

        object? result = null;
        try
        {
            result = await next(context);
            return result;
        }
        finally
        {
            var status = result is IStatusCodeHttpResult { StatusCode: { } code } ? code : result is null ? 500 : http.Response.StatusCode;
            await WriteAsync(http.User, $"{method} {http.Request.Path} → {status}");
        }
    }

    private async Task WriteAsync(ClaimsPrincipal user, string details)
    {
        if (!Guid.TryParse(user.FindFirstValue(AppClaims.OrganizationId), out var orgId))
            return;
        Guid.TryParse(user.FindFirstValue(AppClaims.ImpersonatorId), out var adminId);
        var name = $"{user.FindFirstValue(AppClaims.ImpersonatorName)} (como {user.FindFirstValue(AppClaims.Name)})";
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            PlatformAuditWriter.Add(db, orgId, adminId, name, PlatformActions.ImpersonatedWrite, details, clock.GetUtcNow());
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            // A escrita do cliente já aconteceu; a falha da auditoria vai para o log, sem derrubar a resposta.
            logger.LogError(e, "Falha ao registrar escrita do Entrar como na organização {OrganizationId}", orgId);
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using Contratos.Application.Billing;

namespace Contratos.Api.Endpoints;

/// <summary>
/// Operação da equipe da plataforma (não dos clientes): trocar plano e conceder créditos enquanto
/// não há checkout. Autentica por <c>Authorization: Bearer {Platform:ApiKey}</c>, sem cookie, então
/// fica fora do grupo com antiforgery. Sem a chave configurada, as rotas nem existem.
/// </summary>
internal static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this WebApplication app)
    {
        var key = app.Configuration["Platform:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            return;
        if (key.Trim().Length < 32)
            throw new InvalidOperationException("Platform:ApiKey precisa ter pelo menos 32 caracteres.");
        var expected = Encoding.UTF8.GetBytes(key.Trim());

        var g = app.MapGroup("/api/platform").WithTags("Plataforma").ExcludeFromDescription()
            .AddEndpointFilter(async (ctx, next) =>
            {
                var header = ctx.HttpContext.Request.Headers.Authorization.ToString();
                var given = header.StartsWith("Bearer ", StringComparison.Ordinal) ? Encoding.UTF8.GetBytes(header[7..].Trim()) : [];
                return CryptographicOperations.FixedTimeEquals(given, expected)
                    ? await next(ctx)
                    : ResultHttp.Problem(StatusCodes.Status401Unauthorized, "Chave da plataforma ausente ou inválida.", "unauthorized");
            });

        g.MapGet("/organizations", async (string? q, IPlatformBillingService platform, CancellationToken ct) =>
            TypedResults.Ok(await platform.ListAsync(q, ct)));

        g.MapPut("/organizations/{slug}/plan", async (string slug, ChangePlanRequest req, IPlatformBillingService platform, CancellationToken ct) =>
            (await platform.ChangePlanAsync(slug, req, ct)).ToHttp());

        g.MapPost("/organizations/{slug}/credits", async (string slug, GrantCreditsRequest req, IPlatformBillingService platform, CancellationToken ct) =>
            (await platform.GrantCreditsAsync(slug, req, ct)).ToHttp());
    }
}

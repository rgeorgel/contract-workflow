using System.Security.Claims;
using System.Text.Encodings.Web;
using Contratos.Api.Endpoints;
using Contratos.Infrastructure.Identity;
using Contratos.Infrastructure.Integrations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Contratos.Api.Security;

/// <summary>
/// <c>Authorization: Bearer ctr_...</c> da API pública. Vira um usuário com os mesmos claims do cookie
/// (organização, pessoa e perfil), então os serviços funcionam igual para a tela e para a API.
/// Só vale nas rotas de <c>/api/public</c>: o cookie não autentica lá, e a chave não autentica na API da tela.
/// </summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ApiKeyStore keys)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string KeyIdClaim = "api_key_id";
    public const string KeyNameClaim = "api_key_name";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var identity = await keys.FindAsync(header[7..].Trim(), Context.RequestAborted);
        if (identity is null)
            return AuthenticateResult.Fail("Chave de API inválida ou revogada.");

        var claims = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, identity.UserId.ToString()),
            new Claim(AppClaims.OrganizationId, identity.OrganizationId.ToString()),
            new Claim(AppClaims.Role, identity.Role.ToString()),
            new Claim(KeyIdClaim, identity.KeyId.ToString()),
            new Claim(KeyNameClaim, identity.KeyName),
        ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(claims), SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        await ResultHttp.Problem(StatusCodes.Status401Unauthorized,
            "Chave de API ausente, inválida ou revogada. Envie o cabeçalho Authorization: Bearer ctr_...", "unauthorized")
            .ExecuteAsync(Context);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        ResultHttp.Problem(StatusCodes.Status403Forbidden, "Esta chave não tem acesso a este recurso.", "forbidden").ExecuteAsync(Context);
}

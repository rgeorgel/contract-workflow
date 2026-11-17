using System.Security.Claims;
using System.Text.Encodings.Web;
using Contratos.Api.Endpoints;
using Contratos.Infrastructure.Identity;
using Contratos.Infrastructure.Integrations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Contratos.Api.Security;

/// <summary>
/// Sessão de embed: <c>Authorization: Bearer ces_...</c> nas rotas de <c>/api/embed/v1</c>.
/// Vira o dono da chave de API (como na API pública), com as permissões da sessão como claims.
/// A pré-visualização (carregada pelo <c>src</c> de um iframe, sem cabeçalho) aceita um ticket curto na query.
/// </summary>
internal sealed class EmbedAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, EmbedSessions sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Embed";
    public const string SessionIdClaim = "embed_session_id";
    public const string PermissionClaim = "embed_permission";
    public const string ExternalUserNameClaim = "embed_user_name";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        EmbedIdentity? identity;
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            identity = await sessions.FindAsync(header[7..].Trim(), Context.RequestAborted);
        else if (PreviewTemplateId() is { } templateId && Request.Query["ticket"].ToString() is { Length: > 0 } ticket)
            identity = sessions.ReadPreviewTicket(ticket) is { } t && t.TemplateId == templateId
                ? await sessions.FindByIdAsync(t.SessionId, Context.RequestAborted)
                : null;
        else
            return AuthenticateResult.NoResult();

        if (identity is null)
            return AuthenticateResult.Fail("Sessão de embed inválida ou expirada.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.UserId.ToString()),
            new(AppClaims.OrganizationId, identity.OrganizationId.ToString()),
            new(AppClaims.Role, ApiKeyStore.KeyRole.ToString()),
            new(SessionIdClaim, identity.SessionId.ToString()),
        };
        claims.AddRange(identity.Permissions.Select(p => new Claim(PermissionClaim, p)));
        if (identity.ExternalUserName is { } name)
            claims.Add(new Claim(ExternalUserNameClaim, name));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }

    /// <summary>Só <c>GET /api/embed/v1/templates/{id}/preview</c> aceita ticket.</summary>
    private Guid? PreviewTemplateId()
    {
        if (!HttpMethods.IsGet(Request.Method))
            return null;
        var path = Request.Path.Value ?? "";
        var prefix = EmbedEndpoints.ApiPrefix + "/templates/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith("/preview", StringComparison.Ordinal))
            return null;
        return Guid.TryParse(path.AsSpan(prefix.Length, path.Length - prefix.Length - "/preview".Length), out var id) ? id : null;
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        ResultHttp.Problem(StatusCodes.Status401Unauthorized,
            "A sessão do embed terminou. Abra de novo pelo seu sistema.", "embed_session_expired").ExecuteAsync(Context);

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        ResultHttp.Problem(StatusCodes.Status403Forbidden,
            "Esta sessão não tem permissão para fazer isso.", "embed_permission_denied").ExecuteAsync(Context);
}

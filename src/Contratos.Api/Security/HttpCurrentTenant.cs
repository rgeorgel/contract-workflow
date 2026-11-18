using System.Security.Claims;
using Contratos.Application.Common;
using Contratos.Domain.Users;
using Contratos.Infrastructure.Identity;
using Contratos.Infrastructure.Integrations;

namespace Contratos.Api.Security;

/// <summary>Lê organização, usuário e perfil do cookie da requisição.</summary>
internal sealed class HttpCurrentTenant(IHttpContextAccessor accessor) : ICurrentTenant
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User is { Identity.IsAuthenticated: true } u ? u : null;

    public Guid? OrganizationId => Guid.TryParse(User?.FindFirstValue(AppClaims.OrganizationId), out var id) ? id : null;
    public Guid? UserId => Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public UserRole? Role => Enum.TryParse<UserRole>(User?.FindFirstValue(AppClaims.Role), out var r) ? r : null;
    public string? ApiKeyName => User?.FindFirstValue(ApiKeyAuthenticationHandler.KeyNameClaim);
    public Guid? ApiKeyId => Guid.TryParse(User?.FindFirstValue(ApiKeyAuthenticationHandler.KeyIdClaim), out var id) ? id : null;
    public string? ExternalUserName => User?.FindFirstValue(EmbedAuthenticationHandler.ExternalUserNameClaim);
}

internal static class Policies
{
    public const string IsAdmin = nameof(IsAdmin);
    public const string CanEditContracts = nameof(CanEditContracts);
    /// <summary>API pública: só a chave de API autentica (o cookie da tela não vale aqui).</summary>
    public const string PublicApi = nameof(PublicApi);
    /// <summary>API do embed: só o token da sessão (nem cookie, nem chave de API).</summary>
    public const string Embed = nameof(Embed);
    public const string EmbedEdit = nameof(EmbedEdit);
    public const string EmbedPublish = nameof(EmbedPublish);
    public const string EmbedArchive = nameof(EmbedArchive);
    /// <summary>Portal super admin: equipe da plataforma, pelo cookie da tela.</summary>
    public const string PlatformAdmin = nameof(PlatformAdmin);

    public static void Configure(Microsoft.AspNetCore.Authorization.AuthorizationOptions o)
    {
        o.AddPolicy(PlatformAdmin, p => p.RequireAuthenticatedUser().RequireClaim(AppClaims.PlatformAdmin, "true"));
        o.AddPolicy(IsAdmin, p => p.RequireAuthenticatedUser().RequireClaim(AppClaims.Role, nameof(UserRole.Admin)));
        o.AddPolicy(CanEditContracts, p => p.RequireAuthenticatedUser()
            .RequireClaim(AppClaims.Role, nameof(UserRole.Admin), nameof(UserRole.Editor)));
        o.AddPolicy(PublicApi, p => p.AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser().RequireClaim(ApiKeyAuthenticationHandler.KeyIdClaim));
        o.AddPolicy(Embed, p => EmbedSession(p));
        o.AddPolicy(EmbedEdit, p => EmbedSession(p).RequireClaim(EmbedAuthenticationHandler.PermissionClaim, EmbedPermissions.Edit));
        o.AddPolicy(EmbedPublish, p => EmbedSession(p).RequireClaim(EmbedAuthenticationHandler.PermissionClaim, EmbedPermissions.Publish));
        o.AddPolicy(EmbedArchive, p => EmbedSession(p).RequireClaim(EmbedAuthenticationHandler.PermissionClaim, EmbedPermissions.Archive));
    }

    private static Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder EmbedSession(Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder p) =>
        p.AddAuthenticationSchemes(EmbedAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser().RequireClaim(EmbedAuthenticationHandler.SessionIdClaim);
}

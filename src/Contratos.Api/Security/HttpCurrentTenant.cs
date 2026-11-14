using System.Security.Claims;
using Contratos.Application.Common;
using Contratos.Domain.Users;
using Contratos.Infrastructure.Identity;

namespace Contratos.Api.Security;

/// <summary>Lê organização, usuário e perfil do cookie da requisição.</summary>
internal sealed class HttpCurrentTenant(IHttpContextAccessor accessor) : ICurrentTenant
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User is { Identity.IsAuthenticated: true } u ? u : null;

    public Guid? OrganizationId => Guid.TryParse(User?.FindFirstValue(AppClaims.OrganizationId), out var id) ? id : null;
    public Guid? UserId => Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public UserRole? Role => Enum.TryParse<UserRole>(User?.FindFirstValue(AppClaims.Role), out var r) ? r : null;
}

internal static class Policies
{
    public const string IsAdmin = nameof(IsAdmin);
    public const string CanEditContracts = nameof(CanEditContracts);

    public static void Configure(Microsoft.AspNetCore.Authorization.AuthorizationOptions o)
    {
        o.AddPolicy(IsAdmin, p => p.RequireAuthenticatedUser().RequireClaim(AppClaims.Role, nameof(UserRole.Admin)));
        o.AddPolicy(CanEditContracts, p => p.RequireAuthenticatedUser()
            .RequireClaim(AppClaims.Role, nameof(UserRole.Admin), nameof(UserRole.Editor)));
    }
}

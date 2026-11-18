using System.Security.Claims;
using Contratos.Infrastructure.Platform;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Identity;

/// <summary>
/// Coloca organização, perfil e nome no cookie, para não consultar o banco a cada requisição.
/// Refeito a cada validação do security stamp (1 minuto): mudar <c>Platform:AdminEmails</c> vale em seguida.
/// </summary>
internal sealed class AppUserClaimsPrincipalFactory(
    UserManager<AppUser> userManager, IOptions<IdentityOptions> options, IOptionsMonitor<PlatformOptions> platform)
    : UserClaimsPrincipalFactory<AppUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(AppClaims.OrganizationId, user.OrganizationId.ToString()));
        identity.AddClaim(new Claim(AppClaims.Role, user.Role.ToString()));
        identity.AddClaim(new Claim(AppClaims.Name, user.Name));
        if (user.EmailConfirmed && user.DeactivatedAt is null && platform.CurrentValue.IsAdminEmail(user.Email))
            identity.AddClaim(new Claim(AppClaims.PlatformAdmin, "true"));
        return identity;
    }
}

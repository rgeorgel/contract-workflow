using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Identity;

/// <summary>Coloca organização, perfil e nome no cookie, para não consultar o banco a cada requisição.</summary>
internal sealed class AppUserClaimsPrincipalFactory(UserManager<AppUser> userManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(AppClaims.OrganizationId, user.OrganizationId.ToString()));
        identity.AddClaim(new Claim(AppClaims.Role, user.Role.ToString()));
        identity.AddClaim(new Claim(AppClaims.Name, user.Name));
        return identity;
    }
}

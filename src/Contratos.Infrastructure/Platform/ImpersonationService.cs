using System.Globalization;
using System.Security.Claims;
using Contratos.Application.Common;
using Contratos.Domain.Organizations;
using Contratos.Infrastructure.Identity;
using Contratos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Platform;

/// <param name="Redirect">Para onde a tela vai depois da troca de sessão.</param>
public sealed record ImpersonationResult(string Redirect);

/// <summary>
/// "Entrar como": a pessoa da equipe passa a usar a conta de um usuário do cliente, com acesso total.
/// O cookie é do usuário, mais os claims de quem da equipe está por trás (<see cref="AppClaims.Impersonation"/>).
/// A sessão dura <see cref="Duration"/> e não renova; entrar, sair e cada escrita ficam na auditoria.
/// </summary>
public sealed class ImpersonationService(
    AppDbContext db, SignInManager<AppUser> signIn, ICurrentTenant tenant, IOptionsMonitor<PlatformOptions> platform, TimeProvider clock)
{
    public static readonly TimeSpan Duration = TimeSpan.FromHours(1);

    /// <summary>Por que não dá para entrar como esta pessoa (null: dá).</summary>
    public static string? RefusalReason(AppUser user, Organization org, Guid? currentUserId, PlatformOptions options) =>
        user.Id == currentUserId ? "é a sua própria conta"
        : options.IsAdminEmail(user.Email) ? "é alguém da equipe da plataforma"
        : user.DeactivatedAt is not null ? "o usuário está desativado"
        : user.PasswordHash is null ? "o convite ainda não foi aceito"
        : !user.EmailConfirmed ? "o e-mail não foi confirmado"
        : org.IsSuspended ? "a organização está suspensa"
        : null;

    public static bool IsImpersonating(ClaimsPrincipal principal) => principal.HasClaim(c => c.Type == AppClaims.ImpersonatorId);

    public async Task<Result<ImpersonationResult>> StartAsync(Guid organizationId, Guid userId, CancellationToken ct)
    {
        var org = await db.Organizations.SingleOrDefaultAsync(o => o.Id == organizationId, ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.OrganizationId == organizationId, ct);
        if (org is null || user is null)
            return Error.NotFound("Usuário não encontrado nesta organização.");
        var admin = await db.Users.AsNoTracking().SingleAsync(u => u.Id == tenant.RequireUserId(), ct);
        if (RefusalReason(user, org, admin.Id, platform.CurrentValue) is { } reason)
            return Error.Conflict($"Não dá para entrar como esta pessoa: {reason}.", "cannot_impersonate");

        var now = clock.GetUtcNow();
        var expires = now + Duration;
        PlatformAuditWriter.Add(db, org.Id, admin.Id, PlatformAuditWriter.ActorLabel(admin.Name, admin.Email),
            PlatformActions.Impersonated, $"Entrou como {user.Name} <{user.Email}> ({user.Role})", now);
        await db.SaveChangesAsync(ct);

        await signIn.SignInWithClaimsAsync(user,
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = expires, AllowRefresh = false },
            [
                new Claim(AppClaims.ImpersonatorId, admin.Id.ToString()),
                new Claim(AppClaims.ImpersonatorName, admin.Name),
                new Claim(AppClaims.ImpersonationExpires, expires.ToString("O", CultureInfo.InvariantCulture)),
            ]);
        return new ImpersonationResult("/dashboard.html");
    }

    /// <summary>Volta para a conta da equipe (se ela ainda tiver acesso ao portal; senão, só sai).</summary>
    public async Task<Result<ImpersonationResult>> StopAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(AppClaims.ImpersonatorId), out var adminId)
            || !Guid.TryParse(principal.FindFirstValue(AppClaims.OrganizationId), out var organizationId))
            return Error.Conflict("Esta sessão não é de \"Entrar como\".", "not_impersonating");

        var admin = await db.Users.SingleOrDefaultAsync(u => u.Id == adminId, ct);
        PlatformAuditWriter.Add(db, organizationId, adminId,
            admin is null ? principal.FindFirstValue(AppClaims.ImpersonatorName) ?? "?" : PlatformAuditWriter.ActorLabel(admin.Name, admin.Email),
            PlatformActions.ImpersonationEnded, null, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);

        if (admin is null || admin.DeactivatedAt is not null || !admin.EmailConfirmed || !platform.CurrentValue.IsAdminEmail(admin.Email))
        {
            await signIn.SignOutAsync();
            return new ImpersonationResult("/login.html");
        }
        await signIn.SignInAsync(admin, isPersistent: false);
        return new ImpersonationResult($"/superadmin/tenant.html?id={organizationId}");
    }
}

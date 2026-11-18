using System.Globalization;
using System.Security.Claims;
using Contratos.Application.Common;
using Contratos.Application.Platform;
using Contratos.Domain.Organizations;
using Contratos.Infrastructure.Identity;
using Contratos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Infrastructure.Platform;

/// <param name="Redirect">Para onde a tela vai depois da troca de sessão.</param>
public sealed record ImpersonationResult(string Redirect);

/// <summary>
/// "Entrar como": a conta do portal super admin abre uma sessão do app dos clientes como um usuário, com acesso total.
/// É a única ponte entre os dois: o cookie criado é o do app (do usuário), mais os claims de quem da equipe está por
/// trás (<see cref="AppClaims.Impersonation"/>); o cookie do portal não muda. A sessão dura <see cref="Duration"/>,
/// não renova, e entrada, saída e cada escrita ficam na auditoria.
/// </summary>
public sealed class ImpersonationService(
    AppDbContext db, SignInManager<AppUser> signIn, ICurrentPlatformAdmin admin, TimeProvider clock)
{
    public static readonly TimeSpan Duration = TimeSpan.FromHours(1);
    public const string AppHome = "/dashboard.html";
    public const string AfterEnd = "/login.html";

    /// <summary>Por que não dá para entrar como esta pessoa (null: dá).</summary>
    public static string? RefusalReason(AppUser user, Organization org) =>
        user.DeactivatedAt is not null ? "o usuário está desativado"
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
        if (admin.Id is not { } adminId)
            return Error.Forbidden("Só uma conta do portal super admin pode usar o \"Entrar como\".");
        if (RefusalReason(user, org) is { } reason)
            return Error.Conflict($"Não dá para entrar como esta pessoa: {reason}.", "cannot_impersonate");

        var now = clock.GetUtcNow();
        var expires = now + Duration;
        PlatformAuditWriter.Add(db, org.Id, adminId, PlatformAuditWriter.ActorLabel(admin.Name ?? "?", admin.Email),
            PlatformActions.Impersonated, $"Entrou como {user.Name} <{user.Email}> ({user.Role})", now);
        await db.SaveChangesAsync(ct);

        await signIn.SignInWithClaimsAsync(user,
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = expires, AllowRefresh = false },
            [
                new Claim(AppClaims.ImpersonatorId, adminId.ToString()),
                new Claim(AppClaims.ImpersonatorName, admin.Name ?? "Equipe"),
                new Claim(AppClaims.ImpersonationExpires, expires.ToString("O", CultureInfo.InvariantCulture)),
            ]);
        return new ImpersonationResult(AppHome);
    }

    /// <summary>Encerra a sessão de suporte no app (a do portal continua aberta na outra aba).</summary>
    public async Task<Result<ImpersonationResult>> StopAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(AppClaims.ImpersonatorId), out var adminId)
            || !Guid.TryParse(principal.FindFirstValue(AppClaims.OrganizationId), out var organizationId))
            return Error.Conflict("Esta sessão não é de \"Entrar como\".", "not_impersonating");

        var who = await db.PlatformAdmins.Where(a => a.Id == adminId).Select(a => new { a.Name, a.Email }).SingleOrDefaultAsync(ct);
        PlatformAuditWriter.Add(db, organizationId, adminId,
            who is null ? principal.FindFirstValue(AppClaims.ImpersonatorName) ?? "?" : PlatformAuditWriter.ActorLabel(who.Name, who.Email),
            PlatformActions.ImpersonationEnded, null, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await signIn.SignOutAsync();
        return new ImpersonationResult(AfterEnd);
    }
}

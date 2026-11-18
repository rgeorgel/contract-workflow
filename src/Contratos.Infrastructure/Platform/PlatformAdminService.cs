using System.Net.Mail;
using Contratos.Application.Common;
using Contratos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Infrastructure.Platform;

public sealed record PlatformAdminDto(Guid Id, string Email, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt, bool Active);

public enum AdminLoginOutcome { Success, Invalid, LockedOut }

public sealed record AdminLoginResult(AdminLoginOutcome Outcome, PlatformAdmin? Admin = null);

/// <summary>Contas do portal super admin: login (com bloqueio por tentativas) e gestão pela linha de comando.</summary>
public sealed class PlatformAdminService(AppDbContext db, TimeProvider clock)
{
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private static readonly PasswordHasher<PlatformAdmin> Hasher = new();
    /// <summary>Hash de uma senha qualquer: e-mail inexistente gasta o mesmo tempo que senha errada.</summary>
    private static readonly string DummyHash = Hasher.HashPassword(new PlatformAdmin(), Guid.NewGuid().ToString());

    public async Task<AdminLoginResult> LoginAsync(string? email, string? password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            return new(AdminLoginOutcome.Invalid);
        var normalized = PlatformAdmin.Normalize(email);
        var admin = await db.PlatformAdmins.SingleOrDefaultAsync(a => a.Email == normalized, ct);
        if (admin is null || admin.DeactivatedAt is not null)
        {
            Hasher.VerifyHashedPassword(new PlatformAdmin(), DummyHash, password);
            return new(AdminLoginOutcome.Invalid);
        }

        var now = clock.GetUtcNow();
        if (admin.LockoutEnd > now)
            return new(AdminLoginOutcome.LockedOut);

        var check = Hasher.VerifyHashedPassword(admin, admin.PasswordHash, password);
        if (check == PasswordVerificationResult.Failed)
        {
            admin.FailedLoginCount++;
            if (admin.FailedLoginCount >= MaxFailedLogins)
            {
                admin.LockoutEnd = now + LockoutDuration;
                admin.FailedLoginCount = 0;
            }
            await db.SaveChangesAsync(ct);
            return new(admin.LockoutEnd > now ? AdminLoginOutcome.LockedOut : AdminLoginOutcome.Invalid);
        }

        if (check == PasswordVerificationResult.SuccessRehashNeeded)
            admin.PasswordHash = Hasher.HashPassword(admin, password);
        admin.FailedLoginCount = 0;
        admin.LockoutEnd = null;
        admin.LastLoginAt = now;
        await db.SaveChangesAsync(ct);
        return new(AdminLoginOutcome.Success, admin);
    }

    /// <summary>Conta ativa com o mesmo security stamp do cookie (senha trocada ou conta desativada derrubam a sessão).</summary>
    public Task<bool> IsSessionValidAsync(Guid id, string? stamp, CancellationToken ct) =>
        db.PlatformAdmins.AnyAsync(a => a.Id == id && a.SecurityStamp == stamp && a.DeactivatedAt == null, ct);

    public async Task<Result<PlatformAdminDto>> CreateAsync(string email, string name, string password, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (!IsEmail(email))
            errors.Add("email", "E-mail inválido.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            errors.Add("name", "Informe o nome (até 100 caracteres).");
        if (PasswordError(password) is { } passwordError)
            errors.Add("password", passwordError);
        if (!errors.IsEmpty)
            return Error.Validation(errors);

        var normalized = PlatformAdmin.Normalize(email);
        if (await db.PlatformAdmins.AnyAsync(a => a.Email == normalized, ct))
            return Error.Conflict("Já existe uma conta de super admin com este e-mail.", "email_taken");

        var admin = new PlatformAdmin { Email = normalized, Name = name.Trim(), CreatedAt = clock.GetUtcNow() };
        admin.PasswordHash = Hasher.HashPassword(admin, password);
        db.PlatformAdmins.Add(admin);
        await db.SaveChangesAsync(ct);
        return ToDto(admin);
    }

    public async Task<Result<PlatformAdminDto>> SetPasswordAsync(string email, string password, CancellationToken ct)
    {
        if (PasswordError(password) is { } passwordError)
            return Error.Validation("password", passwordError);
        var admin = await FindAsync(email, ct);
        if (admin is null)
            return Error.NotFound("Conta de super admin não encontrada.");
        admin.PasswordHash = Hasher.HashPassword(admin, password);
        admin.SecurityStamp = Guid.NewGuid().ToString("N");
        admin.FailedLoginCount = 0;
        admin.LockoutEnd = null;
        await db.SaveChangesAsync(ct);
        return ToDto(admin);
    }

    public async Task<Result<PlatformAdminDto>> SetActiveAsync(string email, bool active, CancellationToken ct)
    {
        var admin = await FindAsync(email, ct);
        if (admin is null)
            return Error.NotFound("Conta de super admin não encontrada.");
        admin.DeactivatedAt = active ? null : clock.GetUtcNow();
        admin.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);
        return ToDto(admin);
    }

    public async Task<IReadOnlyList<PlatformAdminDto>> ListAsync(CancellationToken ct) =>
        (await db.PlatformAdmins.AsNoTracking().OrderBy(a => a.Email).ToListAsync(ct)).Select(ToDto).ToList();

    private Task<PlatformAdmin?> FindAsync(string email, CancellationToken ct)
    {
        var normalized = PlatformAdmin.Normalize(email);
        return db.PlatformAdmins.SingleOrDefaultAsync(a => a.Email == normalized, ct);
    }

    public static string? PasswordError(string? password) =>
        password is null || password.Length < PlatformAdmin.PasswordMinLength
            ? $"A senha precisa ter pelo menos {PlatformAdmin.PasswordMinLength} caracteres."
            : password.Length > 200 ? "Senha longa demais." : null;

    private static bool IsEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && MailAddress.TryCreate(value.Trim(), out var a) && a.Address == value.Trim();

    private static PlatformAdminDto ToDto(PlatformAdmin a) => new(a.Id, a.Email, a.Name, a.CreatedAt, a.LastLoginAt, a.DeactivatedAt is null);
}

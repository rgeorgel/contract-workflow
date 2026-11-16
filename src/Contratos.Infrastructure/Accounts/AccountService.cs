using Contratos.Application.Accounts;
using Contratos.Application.Common;
using Contratos.Application.Email;
using Contratos.Domain.Organizations;
using Contratos.Domain.Users;
using Contratos.Infrastructure.Identity;
using Contratos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Accounts;

internal sealed class AccountService(
    AppDbContext db,
    UserManager<AppUser> users,
    IEmailSender email,
    IOptions<AppOptions> appOptions,
    IOptions<Billing.BillingOptions> billing,
    Billing.UsageMeter meter,
    ILogger<AccountService> logger) : IAccountService
{
    private AppOptions App => appOptions.Value;

    public async Task<ValidationErrors> RegisterAsync(RegisterOrganizationRequest req, CancellationToken ct)
    {
        var errors = RegisterOrganizationValidator.Validate(req);
        if (!errors.IsEmpty)
            return errors;

        var orgName = req.OrganizationName!.Trim();
        var name = req.Name!.Trim();
        var emailAddress = req.Email!.Trim();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existing = await users.FindByEmailAsync(emailAddress);
        if (existing is not null)
        {
            if (existing.EmailConfirmed)
            {
                // Não revela que o e-mail existe: responde igual e avisa o dono.
                await tx.RollbackAsync(ct);
                await email.SendAsync(
                    AccountEmails.AccountAlreadyExists(App.ProductName, existing.Email!, existing.Name, Url("/login.html")), ct);
                return errors;
            }

            // Conta nunca confirmada: descarta e recria com os dados novos. Assim quem criou
            // a conta antes, com outra senha, não ganha acesso quando o dono confirmar o e-mail.
            await DiscardUnconfirmedAsync(existing, ct);
        }

        var organization = new Organization(orgName, await UniqueSlugAsync(orgName, ct));
        organization.ChangePlan(billing.Value.DefaultPlan);
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(ct);

        var user = new AppUser
        {
            OrganizationId = organization.Id,
            Name = name,
            Email = emailAddress,
            UserName = emailAddress,
            Role = UserRole.Admin,
        };

        var result = await users.CreateAsync(user, req.Password!);
        if (!result.Succeeded)
        {
            await tx.RollbackAsync(ct);
            foreach (var e in result.Errors)
                errors.Add(e.Code.Contains("Password", StringComparison.Ordinal) ? "password" : "email", Translate(e));
            return errors;
        }

        await tx.CommitAsync(ct);
        logger.LogInformation("Organização {OrganizationId} criada", organization.Id);

        await SendConfirmationAsync(user, organization.Name, ct);
        return errors;
    }

    public async Task<bool> ConfirmEmailAsync(Guid userId, string token, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
            return false;
        if (user.EmailConfirmed)
            return true;

        var decoded = UrlTokens.Decode(token);
        if (decoded is null)
            return false;

        var result = await users.ConfirmEmailAsync(user, decoded);
        if (!result.Succeeded)
            return false;
        // Conta confirmada: libera os contratos de boas-vindas para testar (Avulso).
        await meter.GrantWelcomeCreditsAsync(user.OrganizationId, ct);
        return true;
    }

    public async Task ResendConfirmationAsync(string emailAddress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(emailAddress))
            return;

        var user = await users.FindByEmailAsync(emailAddress.Trim());
        // Convidados confirmam pelo link do convite, não por este.
        if (user is null || user.EmailConfirmed || user.IsPendingInvite)
            return;

        var orgName = await db.Organizations
            .Where(o => o.Id == user.OrganizationId)
            .Select(o => o.Name)
            .SingleAsync(ct);

        await SendConfirmationAsync(user, orgName, ct);
    }

    public async Task ForgotPasswordAsync(string emailAddress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(emailAddress))
            return;

        var user = await users.FindByEmailAsync(emailAddress.Trim());
        if (user is null || !user.EmailConfirmed || user.DeactivatedAt is not null)
            return;

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var link = Url($"/reset-password.html?userId={user.Id}&token={UrlTokens.Encode(token)}");
        await email.SendAsync(AccountEmails.ResetPassword(App.ProductName, user.Email!, user.Name, link), ct);
    }

    public async Task<Result> ResetPasswordAsync(Guid userId, string token, string password, CancellationToken ct)
    {
        if ((password ?? "").Length < RegisterOrganizationValidator.PasswordMinLength)
            return Error.Validation("password", $"A senha precisa ter ao menos {RegisterOrganizationValidator.PasswordMinLength} caracteres.");

        var invalid = new Error(ErrorKind.Validation, "Link inválido ou expirado. Peça um novo.", "invalid_token");
        var decoded = UrlTokens.Decode(token);
        var user = await users.FindByIdAsync(userId.ToString());
        if (decoded is null || user is null || user.DeactivatedAt is not null)
            return invalid;

        var result = await users.ResetPasswordAsync(user, decoded, password!);
        if (!result.Succeeded)
        {
            var passwordError = result.Errors.FirstOrDefault(e => e.Code.StartsWith("Password", StringComparison.Ordinal));
            return passwordError is null ? invalid : Error.Validation("password", Translate(passwordError));
        }

        // Quem redefiniu a senha prova que é dono do e-mail: libera um bloqueio por tentativas.
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return Result.Ok();
    }

    private async Task SendConfirmationAsync(AppUser user, string organizationName, CancellationToken ct)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var link = Url($"/signup.html?step=confirm&userId={user.Id}&token={UrlTokens.Encode(token)}");

        await email.SendAsync(
            AccountEmails.ConfirmEmail(App.ProductName, user.Email!, user.Name, organizationName, link), ct);
    }

    private async Task DiscardUnconfirmedAsync(AppUser user, CancellationToken ct)
    {
        var orgId = user.OrganizationId;
        var othersInOrg = await db.Users.AnyAsync(u => u.OrganizationId == orgId && u.Id != user.Id, ct);

        await users.DeleteAsync(user);
        if (!othersInOrg)
            await db.Organizations.Where(o => o.Id == orgId).ExecuteDeleteAsync(ct);
    }

    private async Task<string> UniqueSlugAsync(string organizationName, CancellationToken ct)
    {
        var baseSlug = Slug.From(organizationName);
        if (!await db.Organizations.AnyAsync(o => o.Slug == baseSlug, ct))
            return baseSlug;

        var trimmed = baseSlug.Length > Organization.SlugMaxLength - 7
            ? baseSlug[..(Organization.SlugMaxLength - 7)].TrimEnd('-')
            : baseSlug;
        return $"{trimmed}-{Convert.ToHexStringLower(Guid.NewGuid().ToByteArray(), 0, 3)}";
    }

    private string Url(string pathAndQuery) => App.PublicBaseUrl.TrimEnd('/') + pathAndQuery;

    internal static string Translate(IdentityError e) => e.Code switch
    {
        nameof(IdentityErrorDescriber.PasswordTooShort) =>
            $"A senha precisa ter ao menos {RegisterOrganizationValidator.PasswordMinLength} caracteres.",
        nameof(IdentityErrorDescriber.PasswordRequiresUniqueChars) => "Use caracteres mais variados na senha.",
        nameof(IdentityErrorDescriber.InvalidEmail) => "E-mail inválido.",
        _ => e.Description,
    };
}

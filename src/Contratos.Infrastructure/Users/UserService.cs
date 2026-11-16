using Contratos.Application.Accounts;
using Contratos.Application.Common;
using Contratos.Application.Email;
using Contratos.Application.Users;
using Contratos.Infrastructure.Accounts;
using Contratos.Infrastructure.Billing;
using Contratos.Infrastructure.Identity;
using Contratos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Users;

internal sealed class UserService(
    AppDbContext db,
    UserManager<AppUser> users,
    ICurrentTenant tenant,
    IEmailSender email,
    IOptions<AppOptions> appOptions,
    UsageMeter meter) : IUserService
{
    public async Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken ct)
    {
        var orgId = tenant.RequireOrganizationId();
        var list = await db.Users
            .Where(u => u.OrganizationId == orgId)
            .OrderBy(u => u.Name == "").ThenBy(u => u.Name).ThenBy(u => u.Email)
            .ToListAsync(ct);
        return list.Select(ToItem).ToList();
    }

    public async Task<Result<UserListItem>> InviteAsync(InviteUserRequest req, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var emailAddress = req.Email?.Trim() ?? "";
        var name = req.Name?.Trim() ?? "";
        if (!RegisterOrganizationValidator.IsValidEmail(emailAddress))
            errors.Add("email", "E-mail inválido.");
        if (name.Length > RegisterOrganizationValidator.NameMaxLength)
            errors.Add("name", $"Use no máximo {RegisterOrganizationValidator.NameMaxLength} caracteres.");
        if (req.Role is null || !Enum.IsDefined(req.Role.Value))
            errors.Add("role", "Escolha o perfil.");
        if (!errors.IsEmpty)
            return Error.Validation(errors);

        var orgId = tenant.RequireOrganizationId();
        var existing = await users.FindByEmailAsync(emailAddress);
        if (existing is not null)
        {
            if (existing.OrganizationId != orgId || !existing.IsPendingInvite)
                return Error.Conflict("Este e-mail já tem uma conta.", "email_in_use");

            // Convite pendente na mesma organização: atualiza e reenvia.
            existing.Role = req.Role!.Value;
            if (name.Length > 0)
                existing.Name = name;
            await users.UpdateAsync(existing);
            await SendInviteAsync(existing, ct);
            return ToItem(existing);
        }

        if (await meter.CheckNotOverdueAsync(orgId, "convidar usuários", ct) is { } overdue)
            return overdue;
        if (await meter.CheckSeatAsync(orgId, ct) is { } noSeat)
            return noSeat;

        var user = new AppUser
        {
            OrganizationId = orgId,
            Name = name,
            Email = emailAddress,
            UserName = emailAddress,
            Role = req.Role!.Value,
            InvitedAt = DateTimeOffset.UtcNow,
            InvitedById = tenant.UserId,
        };
        var result = await users.CreateAsync(user);
        if (!result.Succeeded)
            return Error.Validation("email", AccountService.Translate(result.Errors.First()));

        await SendInviteAsync(user, ct);
        return ToItem(user);
    }

    public async Task<Result> ResendInviteAsync(Guid userId, CancellationToken ct)
    {
        var user = await FindInOrgAsync(userId, ct);
        if (user is null || !user.IsPendingInvite)
            return Error.NotFound("Convite não encontrado.");
        await SendInviteAsync(user, ct);
        return Result.Ok();
    }

    public async Task<Result> CancelInviteAsync(Guid userId, CancellationToken ct)
    {
        var user = await FindInOrgAsync(userId, ct);
        if (user is null || !user.IsPendingInvite)
            return Error.NotFound("Convite não encontrado.");
        await users.DeleteAsync(user);
        return Result.Ok();
    }

    public async Task<Result<UserListItem>> UpdateAsync(Guid userId, UpdateUserRequest req, CancellationToken ct)
    {
        if (userId == tenant.UserId)
            return Error.Forbidden("Você não pode alterar o próprio perfil ou acesso.", "self_change");

        var user = await FindInOrgAsync(userId, ct);
        if (user is null)
            return Error.NotFound("Usuário não encontrado.");

        var changed = false;
        if (req.Role is { } role)
        {
            if (!Enum.IsDefined(role))
                return Error.Validation("role", "Perfil inválido.");
            if (user.Role != role)
            {
                user.Role = role;
                changed = true;
            }
        }

        if (req.Active is { } active)
        {
            if (user.IsPendingInvite)
                return Error.Conflict("Convite pendente: cancele o convite em vez de desativar.", "pending_invite");
            if (active == (user.DeactivatedAt is not null))
            {
                if (active && await meter.CheckNotOverdueAsync(user.OrganizationId, "reativar usuários", ct) is { } overdue)
                    return overdue;
                if (active && await meter.CheckSeatAsync(user.OrganizationId, ct) is { } noSeat)
                    return noSeat;
                user.DeactivatedAt = active ? null : DateTimeOffset.UtcNow;
                changed = true;
            }
        }

        if (changed)
        {
            await users.UpdateAsync(user);
            // Derruba as sessões abertas: o cookie traz perfil e status antigos.
            await users.UpdateSecurityStampAsync(user);
        }

        return ToItem(user);
    }

    public async Task<Result<InviteInfo>> GetInviteAsync(Guid userId, string token, CancellationToken ct)
    {
        var (user, error) = await ValidInviteAsync(userId, token);
        if (user is null)
            return error!;

        var orgName = await db.Organizations.Where(o => o.Id == user.OrganizationId).Select(o => o.Name).SingleAsync(ct);
        return new InviteInfo(orgName, user.Email!, user.Name);
    }

    public async Task<Result<Guid>> AcceptInviteAsync(AcceptInviteRequest req, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var name = req.Name?.Trim() ?? "";
        if (name.Length < 2)
            errors.Add("name", "Informe seu nome.");
        else if (name.Length > RegisterOrganizationValidator.NameMaxLength)
            errors.Add("name", $"Use no máximo {RegisterOrganizationValidator.NameMaxLength} caracteres.");
        if ((req.Password ?? "").Length < RegisterOrganizationValidator.PasswordMinLength)
            errors.Add("password", $"A senha precisa ter ao menos {RegisterOrganizationValidator.PasswordMinLength} caracteres.");
        if (!errors.IsEmpty)
            return Error.Validation(errors);

        var (user, error) = await ValidInviteAsync(req.UserId, req.Token);
        if (user is null)
            return error!;

        user.Name = name;
        user.EmailConfirmed = true;
        // AddPassword grava o usuário e troca o security stamp, invalidando o link do convite.
        var result = await users.AddPasswordAsync(user, req.Password!);
        if (!result.Succeeded)
            return Error.Validation("password", AccountService.Translate(result.Errors.First()));

        return user.Id;
    }

    private async Task<(AppUser? User, Error? Error)> ValidInviteAsync(Guid userId, string? token)
    {
        var invalid = new Error(ErrorKind.NotFound, "Convite inválido, expirado ou já aceito.", "invalid_invite");
        var decoded = UrlTokens.Decode(token);
        if (decoded is null)
            return (null, invalid);

        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsPendingInvite)
            return (null, invalid);

        var ok = await users.VerifyUserTokenAsync(user, TokenPurposes.Invite, TokenPurposes.Invite, decoded);
        return ok ? (user, null) : (null, invalid);
    }

    private async Task SendInviteAsync(AppUser user, CancellationToken ct)
    {
        var app = appOptions.Value;
        var token = await users.GenerateUserTokenAsync(user, TokenPurposes.Invite, TokenPurposes.Invite);
        var link = $"{app.PublicBaseUrl.TrimEnd('/')}/invite.html?userId={user.Id}&token={UrlTokens.Encode(token)}";

        var orgName = await db.Organizations.Where(o => o.Id == user.OrganizationId).Select(o => o.Name).SingleAsync(ct);
        var inviter = tenant.UserId is { } inviterId
            ? await db.Users.Where(u => u.Id == inviterId).Select(u => u.Name).SingleOrDefaultAsync(ct)
            : null;

        await email.SendAsync(
            AccountEmails.Invite(app.ProductName, user.Email!, user.Name, orgName, inviter ?? orgName, link), ct);
    }

    private Task<AppUser?> FindInOrgAsync(Guid userId, CancellationToken ct)
    {
        var orgId = tenant.RequireOrganizationId();
        return db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.OrganizationId == orgId, ct);
    }

    private UserListItem ToItem(AppUser u) => new(
        u.Id, u.Name, u.Email!, u.Role,
        u.DeactivatedAt is not null ? UserStatus.Deactivated : u.IsPendingInvite ? UserStatus.Invited : UserStatus.Active,
        u.CreatedAt, u.Id == tenant.UserId);
}

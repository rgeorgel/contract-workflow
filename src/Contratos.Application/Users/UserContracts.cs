using Contratos.Application.Common;
using Contratos.Domain.Users;

namespace Contratos.Application.Users;

public sealed record UserListItem(
    Guid Id, string Name, string Email, UserRole Role, string Status, DateTimeOffset CreatedAt, bool IsCurrentUser);

public sealed record InviteUserRequest(string? Name, string? Email, UserRole? Role);
public sealed record UpdateUserRequest(UserRole? Role, bool? Active);

public sealed record InviteInfo(string OrganizationName, string Email, string Name);
public sealed record AcceptInviteRequest(Guid UserId, string? Token, string? Name, string? Password);

public static class UserStatus
{
    public const string Active = "Ativo";
    public const string Invited = "Convidado";
    public const string Deactivated = "Desativado";
}

/// <summary>Gestão de usuários da organização atual (Admin) e aceite de convites.</summary>
public interface IUserService
{
    Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken ct);
    Task<Result<UserListItem>> InviteAsync(InviteUserRequest request, CancellationToken ct);
    Task<Result> ResendInviteAsync(Guid userId, CancellationToken ct);
    Task<Result<UserListItem>> UpdateAsync(Guid userId, UpdateUserRequest request, CancellationToken ct);
    Task<Result> CancelInviteAsync(Guid userId, CancellationToken ct);

    Task<Result<InviteInfo>> GetInviteAsync(Guid userId, string token, CancellationToken ct);
    Task<Result<Guid>> AcceptInviteAsync(AcceptInviteRequest request, CancellationToken ct);
}

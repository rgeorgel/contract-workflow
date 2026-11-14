using Contratos.Domain.Users;

namespace Contratos.Application.Common;

/// <summary>Quem está fazendo a requisição. Nulo fora de uma requisição autenticada (worker, webhooks).</summary>
public interface ICurrentTenant
{
    Guid? OrganizationId { get; }
    Guid? UserId { get; }
    UserRole? Role { get; }
}

public static class CurrentTenantExtensions
{
    public static Guid RequireOrganizationId(this ICurrentTenant t) =>
        t.OrganizationId ?? throw new InvalidOperationException("Requisição sem organização.");

    public static Guid RequireUserId(this ICurrentTenant t) =>
        t.UserId ?? throw new InvalidOperationException("Requisição sem usuário.");

    public static bool IsAdmin(this ICurrentTenant t) => t.Role == UserRole.Admin;
    public static bool CanEditContracts(this ICurrentTenant t) => t.Role is UserRole.Admin or UserRole.Editor;
}

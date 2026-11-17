using Contratos.Domain.Users;

namespace Contratos.Application.Common;

/// <summary>Quem está fazendo a requisição. Nulo fora de uma requisição autenticada (worker, webhooks).</summary>
public interface ICurrentTenant
{
    Guid? OrganizationId { get; }
    Guid? UserId { get; }
    UserRole? Role { get; }
    /// <summary>Nome da chave de API, quando a requisição veio da API pública (aparece na linha do tempo).</summary>
    string? ApiKeyName => null;
    /// <summary>Pessoa do sistema do cliente numa sessão de embed (o usuário daqui é o dono da chave).</summary>
    string? ExternalUserName => null;
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

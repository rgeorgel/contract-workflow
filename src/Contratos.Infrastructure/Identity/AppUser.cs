using Contratos.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Contratos.Infrastructure.Identity;

/// <summary>
/// Usuário interno de uma organização. Login local na v1; logins externos (Google)
/// entram depois pela tabela <c>user_logins</c>, sem mudar este modelo.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public AppUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = null!;
    public UserRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Preenchido quando o usuário entrou por convite de um Admin.</summary>
    public DateTimeOffset? InvitedAt { get; set; }
    public Guid? InvitedById { get; set; }

    /// <summary>Usuário desativado não entra; o registro fica para a auditoria.</summary>
    public DateTimeOffset? DeactivatedAt { get; set; }

    public bool IsPendingInvite => InvitedAt is not null && PasswordHash is null;
}

public static class AppClaims
{
    public const string OrganizationId = "org_id";
    public const string Role = "role";
    public const string Name = "name";
}

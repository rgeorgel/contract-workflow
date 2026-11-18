using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contratos.Infrastructure.Platform;

/// <summary>Configuração da equipe da plataforma (não dos clientes).</summary>
public sealed class PlatformOptions
{
    public const string Section = "Platform";

    /// <summary>Chave da API de operação (<c>/api/platform</c>); vazia, as rotas nem existem.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// Conta do portal super admin. Separada dos usuários dos clientes (<c>users</c>): outra tabela, outro login,
/// outro cookie. Não há cadastro pela tela; as contas são criadas pela linha de comando (<c>superadmin create</c>).
/// </summary>
public sealed class PlatformAdmin
{
    public const int PasswordMinLength = 12;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Sempre em minúsculas, sem espaços (é a chave do login).</summary>
    public string Email { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    /// <summary>Muda ao trocar a senha ou desativar: as sessões abertas caem.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}

internal sealed class PlatformAdminConfiguration : IEntityTypeConfiguration<PlatformAdmin>
{
    public void Configure(EntityTypeBuilder<PlatformAdmin> b)
    {
        b.ToTable("platform_admins");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.Email).HasMaxLength(256).IsRequired();
        b.HasIndex(a => a.Email).IsUnique();
        b.Property(a => a.Name).HasMaxLength(100).IsRequired();
        b.Property(a => a.PasswordHash).IsRequired();
        b.Property(a => a.SecurityStamp).HasMaxLength(64).IsRequired();
    }
}

/// <summary>Ação da equipe da plataforma numa organização (plano, créditos, suspensão), para auditoria.</summary>
public sealed class PlatformAuditEntry
{
    public long Id { get; set; }
    public Guid OrganizationId { get; set; }
    /// <summary>Quem fez: conta do portal (<see cref="PlatformAdmin"/>); null quando veio da API de operação (chave).</summary>
    public Guid? ActorAdminId { get; set; }
    public string ActorName { get; set; } = null!;
    public string Action { get; set; } = null!;
    public string? Details { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class PlatformActions
{
    public const string PlanChanged = "PlanChanged";
    public const string CreditsGranted = "CreditsGranted";
    public const string Suspended = "Suspended";
    public const string Reactivated = "Reactivated";
    public const string Impersonated = "Impersonated";
    public const string ImpersonationEnded = "ImpersonationEnded";
    /// <summary>Uma escrita (POST/PUT/DELETE) feita durante o "Entrar como".</summary>
    public const string ImpersonatedWrite = "ImpersonatedWrite";
}

internal sealed class PlatformAuditEntryConfiguration : IEntityTypeConfiguration<PlatformAuditEntry>
{
    public void Configure(EntityTypeBuilder<PlatformAuditEntry> b)
    {
        b.ToTable("platform_audit_log");
        b.HasKey(e => e.Id);
        b.Property(e => e.ActorName).HasMaxLength(200).IsRequired();
        b.Property(e => e.Action).HasMaxLength(40).IsRequired();
        b.Property(e => e.Details).HasMaxLength(500);
        b.HasOne<Domain.Organizations.Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(e => new { e.OrganizationId, e.CreatedAt });
    }
}

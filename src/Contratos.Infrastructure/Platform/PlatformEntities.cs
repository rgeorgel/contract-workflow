using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contratos.Infrastructure.Platform;

/// <summary>Configuração da equipe da plataforma (não dos clientes).</summary>
public sealed class PlatformOptions
{
    public const string Section = "Platform";

    /// <summary>Chave da API de operação (<c>/api/platform</c>); vazia, as rotas nem existem.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// E-mails com acesso ao portal super admin, separados por vírgula ou ponto e vírgula.
    /// A pessoa entra pelo login normal (com o e-mail confirmado); tirar o e-mail daqui corta o acesso em até 1 minuto.
    /// </summary>
    public string? AdminEmails { get; set; }

    public bool IsAdminEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(AdminEmails)
        && AdminEmails.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(e => string.Equals(e, email.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Ação da equipe da plataforma numa organização (plano, créditos, suspensão), para auditoria.</summary>
public sealed class PlatformAuditEntry
{
    public long Id { get; set; }
    public Guid OrganizationId { get; set; }
    /// <summary>Quem fez: usuário do portal; null quando veio da API de operação (chave).</summary>
    public Guid? ActorUserId { get; set; }
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

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contratos.Infrastructure.Billing;

/// <summary>
/// Créditos avulsos comprados (ou concedidos) de uma vez. <see cref="Remaining"/> cai a cada contrato
/// enviado com eles e volta se o envio for desfeito antes de chegar ao provedor.
/// </summary>
public sealed class CreditGrant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public int Quantity { get; set; }
    public int Remaining { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>"manual" (equipe da plataforma) ou o provedor de pagamento.</summary>
    public string Source { get; set; } = CreditSources.Manual;
    /// <summary>Id externo (pagamento); único por organização, dá a idempotência das compras.</summary>
    public string? Reference { get; set; }
    public string? Note { get; set; }
}

public static class CreditSources
{
    public const string Manual = "manual";
    public const string Stripe = "stripe";
    /// <summary>Créditos de boas-vindas do cadastro (referência fixa: uma vez por organização).</summary>
    public const string Welcome = "welcome";
}

/// <summary>Um contrato enviado, e de onde saiu: cota do plano ou crédito avulso.</summary>
public sealed class UsageRecord
{
    public long Id { get; set; }
    public Guid OrganizationId { get; set; }
    /// <summary>Sem chave estrangeira: o uso continua contando depois que o contrato é excluído.</summary>
    public Guid ContractId { get; set; }
    public string PlanId { get; set; } = null!;
    public UsageSource Source { get; set; }
    public Guid? CreditGrantId { get; set; }
    public DateTimeOffset ConsumedAt { get; set; }
    /// <summary>Envio desfeito antes de chegar ao provedor: não conta e devolve o crédito.</summary>
    public DateTimeOffset? RefundedAt { get; set; }
}

public enum UsageSource { Plan, Credit }

internal sealed class CreditGrantConfiguration : IEntityTypeConfiguration<CreditGrant>
{
    public void Configure(EntityTypeBuilder<CreditGrant> b)
    {
        b.ToTable("credit_grants", t =>
        {
            t.HasCheckConstraint("ck_credit_grants_remaining", "remaining >= 0 AND remaining <= quantity");
            t.HasCheckConstraint("ck_credit_grants_quantity", "quantity > 0");
        });
        b.HasKey(g => g.Id);
        b.Property(g => g.Id).ValueGeneratedNever();
        b.Property(g => g.Source).HasMaxLength(30).IsRequired();
        b.Property(g => g.Reference).HasMaxLength(200);
        b.Property(g => g.Note).HasMaxLength(300);
        b.HasOne<Domain.Organizations.Organization>().WithMany().HasForeignKey(g => g.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(g => new { g.OrganizationId, g.ExpiresAt });
        b.HasIndex(g => new { g.OrganizationId, g.Reference }).IsUnique().HasFilter("reference IS NOT NULL");
    }
}

internal sealed class UsageRecordConfiguration : IEntityTypeConfiguration<UsageRecord>
{
    public void Configure(EntityTypeBuilder<UsageRecord> b)
    {
        b.ToTable("usage_records");
        b.HasKey(u => u.Id);
        b.Property(u => u.PlanId).HasMaxLength(30).IsRequired();
        b.Property(u => u.Source).HasConversion<string>().HasMaxLength(20);
        b.HasOne<Domain.Organizations.Organization>().WithMany().HasForeignKey(u => u.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<CreditGrant>().WithMany().HasForeignKey(u => u.CreditGrantId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(u => new { u.OrganizationId, u.ConsumedAt });
        b.HasIndex(u => u.ContractId);
    }
}

using Contratos.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contratos.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> b)
    {
        b.ToTable("organizations");
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).ValueGeneratedNever();
        b.Property(o => o.Name).HasMaxLength(Organization.NameMaxLength).IsRequired();
        b.Property(o => o.Slug).HasMaxLength(Organization.SlugMaxLength).IsRequired();
        b.HasIndex(o => o.Slug).IsUnique();
        b.Property(o => o.AutentiqueFolderId).HasMaxLength(100);
        b.OwnsOne(o => o.Settings, s => s.ToJson("settings"));
        b.Property(o => o.ContractCounter).HasDefaultValue(0);
        b.Property(o => o.CreatedAt).HasDefaultValueSql("now()");
        b.Property(o => o.PlanId).HasMaxLength(30).HasDefaultValue(Contratos.Domain.Billing.Plans.FreeId).IsRequired();
        b.Ignore(o => o.Plan);
    }
}

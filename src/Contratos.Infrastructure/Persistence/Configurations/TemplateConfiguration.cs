using Contratos.Domain.Organizations;
using Contratos.Domain.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contratos.Infrastructure.Persistence.Configurations;

internal sealed class TemplateConfiguration : IEntityTypeConfiguration<Template>
{
    public void Configure(EntityTypeBuilder<Template> b)
    {
        b.ToTable("templates");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.Name).HasMaxLength(Template.NameMaxLength).IsRequired();
        b.Property(t => t.Description).HasMaxLength(Template.DescriptionMaxLength);
        b.Property(t => t.RowVersion).IsRowVersion();
        b.HasIndex(t => new { t.OrganizationId, t.ArchivedAt, t.Name });
        b.HasOne<Organization>().WithMany().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<TemplateVersion>().WithMany().HasForeignKey(t => t.CurrentVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TemplateVersionConfiguration : IEntityTypeConfiguration<TemplateVersion>
{
    public void Configure(EntityTypeBuilder<TemplateVersion> b)
    {
        b.ToTable("template_versions");
        b.HasKey(v => v.Id);
        b.Property(v => v.Id).ValueGeneratedNever();
        b.Property(v => v.BodyHtml).IsRequired();
        b.Property(v => v.Variables).HasJsonConversion();
        b.Property(v => v.DefaultSignerRoles).HasJsonConversion();
        b.HasIndex(v => new { v.TemplateId, v.Version }).IsUnique();
        // No máximo um rascunho por template.
        b.HasIndex(v => v.TemplateId).IsUnique().HasFilter("published_at IS NULL").HasDatabaseName("ux_template_versions_one_draft");
        b.HasOne<Template>().WithMany().HasForeignKey(v => v.TemplateId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(v => v.IsDraft);
    }
}

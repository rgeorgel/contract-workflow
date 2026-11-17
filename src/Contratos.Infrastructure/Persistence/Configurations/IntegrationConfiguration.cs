using Contratos.Domain.Organizations;
using Contratos.Infrastructure.Identity;
using Contratos.Infrastructure.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contratos.Infrastructure.Persistence.Configurations;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> b)
    {
        b.ToTable("api_keys");
        b.HasKey(k => k.Id);
        b.Property(k => k.Id).ValueGeneratedNever();
        b.Property(k => k.Name).HasMaxLength(ApiKey.NameMaxLength).IsRequired();
        b.Property(k => k.Prefix).HasMaxLength(20).IsRequired();
        b.Property(k => k.Hash).IsRequired();
        // É por ele que cada requisição da API acha a chave.
        b.HasIndex(k => k.Hash).IsUnique();
        b.HasIndex(k => k.OrganizationId);
        b.HasOne<Organization>().WithMany().HasForeignKey(k => k.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AppUser>().WithMany().HasForeignKey(k => k.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> b)
    {
        b.ToTable("webhook_endpoints");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Url).HasMaxLength(WebhookEndpoint.UrlMaxLength).IsRequired();
        b.Property(e => e.Description).HasMaxLength(WebhookEndpoint.DescriptionMaxLength);
        b.Property(e => e.Events).HasColumnType("text[]");
        b.Property(e => e.SecretProtected).IsRequired();
        b.HasIndex(e => e.OrganizationId);
        b.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AppUser>().WithMany().HasForeignKey(e => e.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> b)
    {
        b.ToTable("webhook_events");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Type).HasMaxLength(60).IsRequired();
        b.Property(e => e.Payload).IsRequired();
        b.HasIndex(e => new { e.OrganizationId, e.CreatedAt });
        b.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> b)
    {
        b.ToTable("webhook_deliveries");
        b.HasKey(d => d.Id);
        b.Property(d => d.Status).HasMaxLength(20).IsRequired();
        b.Property(d => d.LastError).HasMaxLength(500);
        b.Property(d => d.ResponseBody).HasMaxLength(1000);
        // A fila do worker: só as pendentes, pela hora da próxima tentativa.
        b.HasIndex(d => d.NextAttemptAt).HasFilter("status = 'pending'");
        b.HasIndex(d => new { d.OrganizationId, d.CreatedAt });
        b.HasIndex(d => d.EndpointId);
        b.HasOne<WebhookEvent>().WithMany().HasForeignKey(d => d.EventId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<WebhookEndpoint>().WithMany().HasForeignKey(d => d.EndpointId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EmbedOriginConfiguration : IEntityTypeConfiguration<EmbedOrigin>
{
    public void Configure(EntityTypeBuilder<EmbedOrigin> b)
    {
        b.ToTable("embed_origins");
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).ValueGeneratedNever();
        b.Property(o => o.Origin).HasMaxLength(EmbedOrigin.MaxLength).IsRequired();
        b.HasIndex(o => new { o.OrganizationId, o.Origin }).IsUnique();
        b.HasOne<Organization>().WithMany().HasForeignKey(o => o.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AppUser>().WithMany().HasForeignKey(o => o.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EmbedSessionConfiguration : IEntityTypeConfiguration<EmbedSession>
{
    public void Configure(EntityTypeBuilder<EmbedSession> b)
    {
        b.ToTable("embed_sessions");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.Permissions).HasColumnType("text[]");
        b.Property(s => s.ExternalUserId).HasMaxLength(EmbedSession.ExternalIdMaxLength);
        b.Property(s => s.ExternalUserName).HasMaxLength(Domain.Templates.TemplateVersion.ExternalAuthorMaxLength);
        b.Property(s => s.ParentOrigin).HasMaxLength(EmbedOrigin.MaxLength);
        b.Property(s => s.LaunchTokenHash).IsRequired();
        // Cada requisição da página acha a sessão pelo hash do token de acesso.
        b.HasIndex(s => s.AccessTokenHash).IsUnique().HasFilter("access_token_hash IS NOT NULL");
        b.HasIndex(s => s.ExpiresAt);
        b.HasIndex(s => s.OrganizationId);
        b.HasOne<Organization>().WithMany().HasForeignKey(s => s.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApiKey>().WithMany().HasForeignKey(s => s.ApiKeyId).OnDelete(DeleteBehavior.Cascade);
    }
}

using Contratos.Domain.Contracts;
using Contratos.Domain.Organizations;
using Contratos.Domain.Templates;
using Contratos.Infrastructure.Identity;
using Contratos.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace Contratos.Infrastructure.Persistence.Configurations;

internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> b)
    {
        b.ToTable("contracts");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.Title).HasMaxLength(Contract.TitleMaxLength).IsRequired();
        b.Property(c => c.Message).HasMaxLength(Contract.MessageMaxLength);
        b.Property(c => c.VariableValues).HasJsonConversion();
        b.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.SigningMode).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.DocumentKey).HasMaxLength(300);
        b.Property(c => c.SignedDocumentKey).HasMaxLength(300);
        b.Property(c => c.DocumentSha256).HasMaxLength(64);
        b.Property(c => c.AutentiqueDocumentId).HasMaxLength(100);
        b.Property(c => c.SendError).HasMaxLength(500);
        b.Property(c => c.SendIdempotencyKey).HasMaxLength(100);
        b.Property(c => c.ExternalReference).HasMaxLength(Contract.ExternalReferenceMaxLength);
        b.Property(c => c.ApiIdempotencyKey).HasMaxLength(100);
        b.Property(c => c.RowVersion).IsRowVersion();

        b.HasIndex(c => new { c.OrganizationId, c.Status, c.CreatedAt }).IsDescending(false, false, true);
        b.HasIndex(c => new { c.OrganizationId, c.Number }).IsUnique();
        // É por ele que o webhook encontra o contrato (e a organização).
        b.HasIndex(c => c.AutentiqueDocumentId).IsUnique();
        b.HasIndex(c => new { c.OrganizationId, c.ExternalReference }).HasFilter("external_reference IS NOT NULL");
        b.HasIndex(c => new { c.OrganizationId, c.ApiIdempotencyKey }).IsUnique().HasFilter("api_idempotency_key IS NOT NULL");

        b.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<TemplateVersion>().WithMany().HasForeignKey(c => c.TemplateVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(c => c.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(c => c.Signers).WithOne().HasForeignKey(s => s.ContractId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(c => c.Signers).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(c => c.IsDraft);
        b.Ignore(c => c.IsOpen);
        b.Ignore(c => c.IsFinal);

        // Busca: título, número, valores das variáveis e signatários. Preenchido por contracts_search_refresh().
        b.Property<NpgsqlTsVector>(SearchVector).HasColumnType("tsvector");
        b.HasIndex(SearchVector).HasMethod("GIN");
    }

    public const string SearchVector = "SearchVector";
}

internal sealed class ContractSignerConfiguration : IEntityTypeConfiguration<ContractSigner>
{
    public void Configure(EntityTypeBuilder<ContractSigner> b)
    {
        b.ToTable("contract_signers");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.Name).HasMaxLength(100).IsRequired();
        b.Property(s => s.Email).HasMaxLength(256);
        b.Property(s => s.Phone).HasMaxLength(20);
        b.Property(s => s.Cpf).HasMaxLength(11);
        b.Property(s => s.Role).HasMaxLength(60);
        b.Property(s => s.DeliveryMethod).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.Action).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.AutentiquePublicId).HasMaxLength(100);
        b.Property(s => s.ShortLink).HasMaxLength(300);
        b.Property(s => s.DeclineReason).HasMaxLength(500);
        b.HasIndex(s => s.ContractId);
        b.HasIndex(s => s.AutentiquePublicId);
        b.Ignore(s => s.CanResend);
    }
}

internal sealed class ContractEventConfiguration : IEntityTypeConfiguration<ContractEvent>
{
    public void Configure(EntityTypeBuilder<ContractEvent> b)
    {
        b.ToTable("contract_events");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).UseIdentityByDefaultColumn();
        b.Property(e => e.Type).HasMaxLength(40).IsRequired();
        b.Property(e => e.Payload).HasJsonConversion();
        b.HasIndex(e => new { e.ContractId, e.OccurredAt });
        b.HasOne<Contract>().WithMany().HasForeignKey(e => e.ContractId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(m => m.Id);
        b.Property(m => m.Type).HasMaxLength(60).IsRequired();
        b.Property(m => m.Payload).HasColumnType("jsonb");
        b.Property(m => m.LastError).HasMaxLength(2000);
        b.HasIndex(m => m.NextAttemptAt).HasFilter("processed_at IS NULL").HasDatabaseName("ix_outbox_messages_pending");
    }
}

internal sealed class WebhookInboxConfiguration : IEntityTypeConfiguration<WebhookInboxItem>
{
    public void Configure(EntityTypeBuilder<WebhookInboxItem> b)
    {
        b.ToTable("webhook_inbox");
        b.HasKey(w => w.Id);
        b.Property(w => w.Provider).HasMaxLength(30).IsRequired();
        b.Property(w => w.ExternalEventId).HasMaxLength(200).IsRequired();
        b.Property(w => w.Payload).HasColumnType("jsonb");
        b.Property(w => w.LastError).HasMaxLength(2000);
        b.HasIndex(w => new { w.Provider, w.ExternalEventId }).IsUnique();
        b.HasIndex(w => w.ReceivedAt).HasFilter("processed_at IS NULL").HasDatabaseName("ix_webhook_inbox_pending");
    }
}

internal sealed class DeletionLogConfiguration : IEntityTypeConfiguration<DeletionLogEntry>
{
    public void Configure(EntityTypeBuilder<DeletionLogEntry> b)
    {
        b.ToTable("deletion_log");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).ValueGeneratedNever();
        b.Property(d => d.ContractTitle).HasMaxLength(Contract.TitleMaxLength).IsRequired();
        b.HasIndex(d => new { d.OrganizationId, d.DeletedAt });
    }
}

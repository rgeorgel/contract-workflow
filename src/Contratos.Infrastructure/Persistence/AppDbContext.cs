using Contratos.Application.Common;
using Contratos.Domain.Contracts;
using Contratos.Domain.Organizations;
using Contratos.Domain.Templates;
using Contratos.Infrastructure.Identity;
using Contratos.Infrastructure.Messaging;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant? tenant = null)
    : IdentityUserContext<AppUser, Guid>(options)
{
    /// <summary>
    /// Organização da requisição atual, usada pelos filtros globais. Sem tenant (worker, webhook)
    /// os filtros não deixam passar nada; quem precisa enxergar tudo usa <c>IgnoreQueryFilters()</c>.
    /// Atenção: <c>IgnoreQueryFilters()</c> vale para a consulta inteira, mesmo dentro de uma subconsulta.
    /// Use só em código sem tenant (worker, webhooks), nunca em consultas de uma requisição.
    /// </summary>
    public Guid? CurrentOrganizationId => tenant?.OrganizationId;

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<TemplateVersion> TemplateVersions => Set<TemplateVersion>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractSigner> ContractSigners => Set<ContractSigner>();
    public DbSet<ContractEvent> ContractEvents => Set<ContractEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<WebhookInboxItem> WebhookInbox => Set<WebhookInboxItem>();
    public DbSet<DeletionLogEntry> DeletionLog => Set<DeletionLogEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Isolamento por organização. Versões de template são lidas sempre a partir do template.
        builder.Entity<Template>().HasQueryFilter(t => t.OrganizationId == CurrentOrganizationId);
        builder.Entity<Contract>().HasQueryFilter(c => c.OrganizationId == CurrentOrganizationId);
    }
}

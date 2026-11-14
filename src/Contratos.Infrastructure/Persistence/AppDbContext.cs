using Contratos.Application.Common;
using Contratos.Domain.Organizations;
using Contratos.Domain.Templates;
using Contratos.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant? tenant = null)
    : IdentityUserContext<AppUser, Guid>(options)
{
    /// <summary>
    /// Organização da requisição atual, usada pelos filtros globais. Sem tenant (worker, webhook)
    /// os filtros não deixam passar nada; quem precisa enxergar tudo usa <c>IgnoreQueryFilters()</c>.
    /// </summary>
    public Guid? CurrentOrganizationId => tenant?.OrganizationId;

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<TemplateVersion> TemplateVersions => Set<TemplateVersion>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Isolamento por organização. Versões de template são lidas sempre a partir do template.
        builder.Entity<Template>().HasQueryFilter(t => t.OrganizationId == CurrentOrganizationId);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Contratos.Infrastructure.Persistence;

/// <summary>Usado só pelo <c>dotnet ef</c>; não precisa de banco rodando para gerar migrations.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        DependencyInjection.ConfigureDbContext(options, "Host=localhost;Port=5434;Database=contratos;Username=contratos;Password=design");
        return new AppDbContext(options.Options);
    }
}

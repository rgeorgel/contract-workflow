using Contratos.Application.Accounts;
using Contratos.Application.Email;
using Contratos.Application.Organizations;
using Contratos.Application.Users;
using Contratos.Infrastructure.Accounts;
using Contratos.Infrastructure.Email;
using Contratos.Infrastructure.Identity;
using Contratos.Infrastructure.Organizations;
using Contratos.Infrastructure.Users;
using Contratos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Contratos.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default não configurada.");

        services.AddDbContext<AppDbContext>(o => ConfigureDbContext(o, connectionString));

        services.AddOptions<AppOptions>().Bind(config.GetSection(AppOptions.Section));
        services.AddOptions<SmtpOptions>().Bind(config.GetSection(SmtpOptions.Section));

        services
            .AddIdentityCore<AppUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.SignIn.RequireConfirmedEmail = true;
                o.Password.RequiredLength = RegisterOrganizationValidator.PasswordMinLength;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredUniqueChars = 1;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
                o.Tokens.PasswordResetTokenProvider = TokenPurposes.PasswordReset;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<InviteTokenProvider<AppUser>>(TokenPurposes.Invite)
            .AddTokenProvider<PasswordResetTokenProvider<AppUser>>(TokenPurposes.PasswordReset);

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(WithDefaults(connectionString), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();

    /// <summary>Sem Kerberos na imagem: desliga a tentativa de criptografia GSS, que só gera erro no log.</summary>
    private static string WithDefaults(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!connectionString.Contains("Gss", StringComparison.OrdinalIgnoreCase))
            builder.GssEncryptionMode = GssEncryptionMode.Disable;
        return builder.ConnectionString;
    }
}

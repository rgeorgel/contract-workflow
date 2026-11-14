using Contratos.Application.Accounts;
using Contratos.Application.Common;
using Contratos.Application.Contracts;
using Contratos.Application.Dashboard;
using Contratos.Application.Signatures;
using Contratos.Application.Storage;
using Contratos.Application.Email;
using Contratos.Application.Templates;
using Contratos.Application.Organizations;
using Contratos.Application.Users;
using Contratos.Infrastructure.Accounts;
using Contratos.Infrastructure.Email;
using Contratos.Infrastructure.Contracts;
using Contratos.Infrastructure.Dashboard;
using Contratos.Infrastructure.Identity;
using Contratos.Infrastructure.Messaging;
using Contratos.Infrastructure.Signatures;
using Contratos.Infrastructure.Storage;
using Contratos.Infrastructure.Organizations;
using Contratos.Infrastructure.Security;
using Contratos.Infrastructure.Templates;
using Contratos.Infrastructure.Users;
using Contratos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        services.AddScoped<ITemplateService, TemplateService>();
        services.AddSingleton<IHtmlSanitizer, TemplateHtmlSanitizer>();
        services.AddSingleton<DocumentRenderer>();

        // Contratos, arquivos, PDF e assinatura
        services.AddOptions<ContractOptions>().Bind(config.GetSection(ContractOptions.Section));
        services.AddOptions<StorageOptions>().Bind(config.GetSection(StorageOptions.Section));
        services.AddOptions<GotenbergOptions>().Bind(config.GetSection(GotenbergOptions.Section));
        services.AddOptions<FakeSignatureOptions>().Bind(config.GetSection(FakeSignatureOptions.Section));
        services.AddOptions<WorkerOptions>().Bind(config.GetSection(WorkerOptions.Section));
        services.AddScoped<IContractService, ContractService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddSingleton<IFileStorage, S3FileStorage>();
        services.AddHostedService<StorageInitializer>();
        services.AddHttpClient<IPdfRenderer, GotenbergPdfRenderer>((sp, http) =>
        {
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<GotenbergOptions>>().Value.Url);
            http.Timeout = TimeSpan.FromSeconds(60);
        });

        var signatureProvider = config["Signature:Provider"] ?? "Fake";
        if (!string.Equals(signatureProvider, "Fake", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Provedor de assinatura '{signatureProvider}' ainda não implementado (Autentique entra na fase 4). Use Signature__Provider=Fake.");
        services.AddSingleton<FakeSignatureProvider>();
        services.AddSingleton<ISignatureProvider>(sp => sp.GetRequiredService<FakeSignatureProvider>());

        services.AddScoped<WebhookReceiver>();
        services.AddSingleton<BackgroundProcessor>();
        services.AddHostedService<BackgroundWorker>();
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

using Contratos.Application.Accounts;
using Contratos.Application.Billing;
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
using Contratos.Infrastructure.Billing;
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
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
        services.AddOptions<Platform.PlatformOptions>().Bind(config.GetSection(Platform.PlatformOptions.Section));
        services.AddOptions<EmailOptions>().Bind(config.GetSection(EmailOptions.Section));
        services.AddOptions<SmtpOptions>().Bind(config.GetSection(SmtpOptions.Section));
        services.AddOptions<ResendOptions>().Bind(config.GetSection(ResendOptions.Section));

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
        services.AddOptions<BillingOptions>().Bind(config.GetSection(BillingOptions.Section))
            .Validate(o => Domain.Billing.Plans.Find(o.DefaultPlan) is not null, "Billing:DefaultPlan precisa ser um plano do catálogo.")
            .ValidateOnStart();
        services.AddScoped<UsageMeter>();
        services.AddScoped<BillingReader>();
        services.AddScoped<IBillingService, BillingService>();
        services.AddScoped<IPlatformBillingService, PlatformBillingService>();
        services.AddScoped<Platform.PlatformAuditWriter>();
        services.AddScoped<Platform.ImpersonationService>();
        services.AddScoped<Platform.PlatformAdminService>();
        services.AddScoped<Application.Platform.ISuperAdminService, Platform.SuperAdminService>();
        AddStripe(services, config);
        services.AddScoped<IContractService, ContractService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddSingleton<IFileStorage, S3FileStorage>();
        services.AddHostedService<StorageInitializer>();
        services.AddHttpClient<IPdfRenderer, GotenbergPdfRenderer>((sp, http) =>
        {
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<GotenbergOptions>>().Value.Url);
            http.Timeout = TimeSpan.FromSeconds(60);
        });

        AddSignatureProvider(services, config);
        services.AddOptions<BalanceAlertOptions>().Bind(config.GetSection(BalanceAlertOptions.Section));
        services.AddSingleton<ProviderBalanceMonitor>();

        AddIntegrations(services, config);
        services.AddScoped<WebhookReceiver>();
        services.AddSingleton<ContractAlerts>();
        services.AddSingleton<BackgroundProcessor>();
        services.AddHostedService<BackgroundWorker>();
        AddEmailSender(services, config);

        // "ready": tudo de que o envio de contratos depende. /health/live não olha dependências.
        services.AddHttpClient(nameof(GotenbergHealthCheck));
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: ["ready"])
            .AddCheck<GotenbergHealthCheck>("gotenberg", HealthStatus.Degraded, tags: ["ready"])
            .AddCheck<StorageHealthCheck>("storage", tags: ["ready"])
            .AddCheck<SignatureBalanceHealthCheck>("signature-balance", HealthStatus.Degraded, tags: ["ready"]);

        return services;
    }

    /// <summary>Signature__Provider: Fake (desenvolvimento e testes, com simulador) ou Autentique.</summary>
    private static void AddSignatureProvider(IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<AutentiqueOptions>().Bind(config.GetSection(AutentiqueOptions.Section));
        var provider = config["Signature:Provider"] ?? "Fake";

        if (string.Equals(provider, "Fake", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<FakeSignatureProvider>();
            services.AddSingleton<ISignatureProvider>(sp => sp.GetRequiredService<FakeSignatureProvider>());
            return;
        }
        if (!string.Equals(provider, "Autentique", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Provedor de assinatura '{provider}' desconhecido. Use Signature__Provider=Fake ou Autentique.");

        var autentique = config.GetSection(AutentiqueOptions.Section).Get<AutentiqueOptions>() ?? new AutentiqueOptions();
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(autentique.ApiKey)) missing.Add("Autentique__ApiKey (AUTENTIQUE_API_KEY)");
        if (autentique.WebhookSecrets.Count == 0) missing.Add("Autentique__WebhookSecret (AUTENTIQUE_WEBHOOK_SECRET)");
        if (missing.Count > 0)
            throw new InvalidOperationException($"Signature__Provider=Autentique exige {string.Join(" e ", missing)}.");

        services.AddHttpClient<ISignatureProvider, AutentiqueSignatureProvider>(http =>
        {
            // Upload de até 20 MB; a outbox tenta de novo se passar disso.
            http.Timeout = TimeSpan.FromSeconds(90);
        });
    }

    private static void AddEmailSender(IServiceCollection services, IConfiguration config)
    {
        var email = config.GetSection(EmailOptions.Section).Get<EmailOptions>() ?? new EmailOptions();
        switch (email.Provider)
        {
            case EmailProvider.Smtp:
                services.AddScoped<IEmailSender, SmtpEmailSender>();
                break;
            case EmailProvider.Resend:
                var resend = config.GetSection(ResendOptions.Section).Get<ResendOptions>() ?? new ResendOptions();
                if (string.IsNullOrWhiteSpace(resend.ApiKey))
                    throw new InvalidOperationException("Email__Provider=Resend exige a chave da API em Resend__ApiKey (RESEND_API_KEY no .env).");
                services.AddHttpClient<IEmailSender, ResendEmailSender>(http =>
                {
                    http.BaseAddress = new Uri(resend.BaseUrl);
                    http.Timeout = TimeSpan.FromSeconds(15);
                    http.DefaultRequestHeaders.Authorization = new("Bearer", resend.ApiKey);
                });
                break;
            default:
                throw new InvalidOperationException($"Provedor de e-mail '{email.Provider}' desconhecido. Use Smtp ou Resend.");
        }
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

    /// <summary>API pública (chaves) e webhooks de saída para os sistemas dos clientes.</summary>
    private static void AddIntegrations(IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<Integrations.WebhookOptions>().Bind(config.GetSection(Integrations.WebhookOptions.Section));
        services.AddScoped<Integrations.ApiKeyStore>();
        services.AddScoped<Integrations.EmbedSessions>();
        services.AddSingleton<Integrations.WebhookSecrets>();
        services.AddScoped<Application.Integrations.IIntegrationService, Integrations.IntegrationService>();
        services.AddSingleton<Integrations.WebhookDispatcher>();
        var allowPrivate = config.GetValue<bool>($"{Integrations.WebhookOptions.Section}:AllowPrivateNetworks");
        services.AddHttpClient(Integrations.WebhookDispatcher.HttpClientName, http => http.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Redirecionamento levaria a requisição (assinada) para outro lugar sem a checagem de rede.
                AllowAutoRedirect = false,
                UseCookies = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectCallback = Integrations.WebhookUrls.SafeConnect(allowPrivate),
            });
    }

    private static void AddStripe(IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<Billing.Stripe.StripeOptions>().Bind(config.GetSection(Billing.Stripe.StripeOptions.Section));
        var stripe = config.GetSection(Billing.Stripe.StripeOptions.Section).Get<Billing.Stripe.StripeOptions>() ?? new();
        if (!stripe.Enabled)
        {
            services.AddScoped<ISubscriptionService, Billing.Stripe.DisabledSubscriptionService>();
            return;
        }
        if (string.IsNullOrWhiteSpace(stripe.WebhookSecret))
            throw new InvalidOperationException("Stripe:SecretKey configurada sem Stripe:WebhookSecret: renovações e cancelamentos não chegariam ao sistema.");

        services.AddHttpClient("stripe", http => http.Timeout = TimeSpan.FromSeconds(60));
        services.AddSingleton<global::Stripe.StripeClient>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<Billing.Stripe.StripeOptions>>().Value;
            var http = new global::Stripe.SystemNetHttpClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("stripe"));
            return string.IsNullOrWhiteSpace(o.ApiBase)
                ? new global::Stripe.StripeClient(o.SecretKey!.Trim(), httpClient: http)
                : new global::Stripe.StripeClient(o.SecretKey!.Trim(), httpClient: http, apiBase: o.ApiBase);
        });
        services.AddSingleton<Billing.Stripe.StripeCatalog>();
        services.AddScoped<Billing.Stripe.StripeSubscriptionSync>();
        services.AddScoped<Billing.Stripe.StripeCreditPacks>();
        services.AddScoped<Billing.Stripe.StripeWebhookReceiver>();
        services.AddScoped<ISubscriptionService, Billing.Stripe.StripeSubscriptionService>();
    }
}

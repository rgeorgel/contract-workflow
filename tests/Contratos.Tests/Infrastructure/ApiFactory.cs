using System.Collections.Concurrent;
using Contratos.Application.Email;
using Contratos.Application.Storage;
using Contratos.Infrastructure.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace Contratos.Tests.Infrastructure;

/// <summary>Sobe a API inteira contra um Postgres 17 real (Testcontainers) e captura os e-mails.</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17").Build();

    protected virtual int ApiPermitsPerMinute => 100_000;

    public const string PlatformKey = "test-platform-key-0123456789abcdef0123456789";

    public CapturingEmailSender Emails { get; } = new();
    public InMemoryFileStorage Files { get; } = new();
    public FakeStripe Stripe { get; } = new();

    /// <summary>Roda o worker uma vez: webhooks, outbox e expiração.</summary>
    public Task ProcessBackgroundAsync() => Services.GetRequiredService<BackgroundProcessor>().ProcessAllAsync(CancellationToken.None);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _db.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", "10000");
        builder.UseSetting("RateLimiting:ApiPermitsPerMinute", ApiPermitsPerMinute.ToString());
        builder.UseSetting("Gotenberg:Url", "http://127.0.0.1:9"); // ninguém escuta: health "degraded"
        builder.UseSetting("App:PublicBaseUrl", "https://app.test");
        builder.UseSetting("Worker:Enabled", "false");
        builder.UseSetting("Storage:CreateBucket", "false");
        // Os testes gerais não esbarram em cota; os de planos trocam o plano pela API da plataforma.
        builder.UseSetting("Billing:DefaultPlan", "empresa");
        builder.UseSetting("Platform:ApiKey", PlatformKey);
        builder.UseSetting("Stripe:SecretKey", FakeStripe.SecretKey);
        builder.UseSetting("Stripe:WebhookSecret", FakeStripe.WebhookSecret);
        builder.UseSetting("Stripe:ApiBase", "https://api.stripe.test");
        for (var i = 0; i < 4; i++)
            builder.UseSetting($"Worker:RetryDelaysSeconds:{i}", "0");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.RemoveAll<IFileStorage>();
            services.AddSingleton<IFileStorage>(Files);
            services.RemoveAll<IPdfRenderer>();
            services.AddSingleton<IPdfRenderer, FakePdfRenderer>();
            services.AddHttpClient("stripe").ConfigurePrimaryHttpMessageHandler(() => Stripe);
        });
    }

    /// <summary>Cliente com cookies; HTTPS porque fora de Development os cookies são Secure.</summary>
    public ApiClient CreateApiClient() =>
        new(CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }));

    public Task InitializeAsync() => _db.StartAsync();

    public new virtual async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }
}

public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> To(string email) =>
        _sent.Where(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase)).ToList();
}

public sealed class InMemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new();

    public IReadOnlyCollection<string> Keys => _files.Keys.ToList();

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        _files[key] = ms.ToArray();
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) =>
        Task.FromResult<Stream?>(_files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeletePrefixAsync(string prefix, CancellationToken ct)
    {
        foreach (var key in _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
            _files.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

/// <summary>PDF mínimo com o HTML embutido, para os testes conferirem o conteúdo sem Gotenberg.</summary>
public sealed class FakePdfRenderer : IPdfRenderer
{
    public Task<byte[]> RenderAsync(string html, CancellationToken ct) =>
        Task.FromResult(System.Text.Encoding.UTF8.GetBytes("%PDF-1.4\n%fake\n" + html));
}

/// <summary>Mesma API com limite baixo, para testar o rate limit geral.</summary>
public sealed class LowLimitApiFactory : ApiFactory
{
    protected override int ApiPermitsPerMinute => 5;
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

/// <summary>Mesma API com o provedor Autentique de verdade, falando com a <see cref="FakeAutentique"/> por HTTP.</summary>
public sealed class AutentiqueApiFactory : ApiFactory
{
    public const string AlertEmail = "alertas@plataforma.test";

    public FakeAutentique Autentique { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Signature:Provider", "Autentique");
        builder.UseSetting("Autentique:ApiKey", FakeAutentique.ApiKey);
        builder.UseSetting("Autentique:WebhookSecret", $"{FakeAutentique.WebhookSecret}, {FakeAutentique.SecondWebhookSecret}");
        builder.UseSetting("Autentique:Sandbox", "true");
        builder.UseSetting("Worker:ReconcileIntervalMinutes", "0");
        builder.UseSetting("SignatureBalance:CheckIntervalHours", "0");
        builder.UseSetting("SignatureBalance:AlertEmail", AlertEmail);
        builder.ConfigureTestServices(services =>
            services.AddHttpClient<Contratos.Application.Signatures.ISignatureProvider, Contratos.Infrastructure.Signatures.AutentiqueSignatureProvider>()
                .ConfigurePrimaryHttpMessageHandler(() => Autentique));
    }
}

[CollectionDefinition(Name)]
public sealed class AutentiqueCollection : ICollectionFixture<AutentiqueApiFactory>
{
    public const string Name = "autentique";
}

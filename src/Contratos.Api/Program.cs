using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Contratos.Api.Endpoints;
using Contratos.Api.Security;
using Contratos.Application.Common;
using Contratos.Infrastructure;
using Contratos.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, lc) =>
{
    lc.ReadFrom.Configuration(builder.Configuration).Enrich.FromLogContext();
    if (builder.Environment.IsDevelopment())
        lc.WriteTo.Console();
    else
        lc.WriteTo.Console(new RenderedCompactJsonFormatter());
});

builder.Services.AddInfrastructure(builder.Configuration);

// Chaves dos cookies e dos tokens de e-mail num volume: sobrevivem a restart e deploy.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Contratos");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var secureCookies = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "contratos.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = secureCookies;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
    // API pura: sem redirecionar para página de login, só o status.
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});
builder.Services.AddAuthorization(Policies.Configure);
// Perfil trocado ou usuário desativado: a sessão antiga cai em até 1 minuto.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenant, HttpCurrentTenant>();

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = "contratos.csrf";
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = secureCookies;
});

// Limites: rotas de autenticação por IP (força bruta) e a API inteira por usuário ou IP (abuso, scraping).
var authPermitsPerMinute = builder.Configuration.GetValue("RateLimiting:AuthPermitsPerMinute", 10);
var apiPermitsPerMinute = builder.Configuration.GetValue("RateLimiting:ApiPermitsPerMinute", 300);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.RateLimitPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ClientIp(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermitsPerMinute, Window = TimeSpan.FromMinutes(1) }));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        if (!ctx.Request.Path.StartsWithSegments("/api"))
            return RateLimitPartition.GetNoLimiter("static");
        var key = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value is { } userId
            ? $"user:{userId}"
            : $"ip:{ClientIp(ctx)}";
        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = apiPermitsPerMinute, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6,
        });
    });
});

// Atrás de proxy reverso (TLS no proxy): IP real do cliente e esquema https vêm dos cabeçalhos X-Forwarded-*.
if (builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1;
        // A rede do proxy varia (Docker, nuvem); a app não deve ficar exposta sem ele na frente.
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });
}
builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
    app.UseForwardedHeaders();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseStatusCodePages();
app.UseSecurityHeaders();
app.UseSerilogRequestLogging(o =>
{
    // Só identificadores: nada de e-mail, CPF ou telefone no log (LGPD). A query string não é registrada.
    o.EnrichDiagnosticContext = (diag, http) =>
    {
        if (http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value is { } userId)
            diag.Set("UserId", userId);
        if (http.User.FindFirst(Contratos.Infrastructure.Identity.AppClaims.OrganizationId)?.Value is { } orgId)
            diag.Set("OrganizationId", orgId);
    };
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // Sem bundler nem hash no nome: o navegador revalida por ETag a cada carga.
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});

app.UseAuthentication();
// Antes da autorização: senão um 401 sai antes do limite e chamadas anônimas não são contadas.
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// live: o processo responde (para o orquestrador reiniciar). ready: dependências ok (para receber tráfego).
app.MapHealthChecks("/health/live", HealthResponse.Options(_ => false));
app.MapHealthChecks("/health/ready", HealthResponse.Options(r => r.Tags.Contains("ready")));
app.MapHealthChecks("/health", HealthResponse.Options(r => r.Tags.Contains("ready")));

var api = app.MapGroup("/api/v1").AddEndpointFilter<AntiforgeryFilter>();
api.MapAuthEndpoints();
api.MapUsersEndpoints();
api.MapOrganizationEndpoints();
api.MapTemplatesEndpoints();
api.MapContractsEndpoints();
api.MapDashboardEndpoints();
if (string.Equals(app.Configuration["Signature:Provider"] ?? "Fake", "Fake", StringComparison.OrdinalIgnoreCase))
    api.MapFakeProviderEndpoints();
app.MapWebhookEndpoints();

app.Run();

static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

public partial class Program;

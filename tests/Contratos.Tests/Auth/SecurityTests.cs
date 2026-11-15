using System.Net;
using System.Net.Http.Json;
using Contratos.Tests.Infrastructure;

namespace Contratos.Tests.Auth;

[Collection(ApiCollection.Name)]
public sealed class SecurityTests(ApiFactory factory)
{
    [Fact]
    public async Task Writes_without_antiforgery_token_are_rejected()
    {
        var client = factory.CreateApiClient();

        var res = await client.Http.PostAsJsonAsync("/api/v1/auth/register",
            new { organizationName = "Sem Token", name = "Fulano", email = "sem-token@empresa.com.br", password = "senha-bem-longa-123" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("csrf_invalid", (await res.JsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Me_requires_a_session()
    {
        var res = await factory.CreateApiClient().GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/signup.html")]
    [InlineData("/login.html")]
    public async Task Pages_are_served_with_security_headers(string path)
    {
        var res = await factory.CreateApiClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
        var csp = string.Join(';', res.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Liveness_does_not_depend_on_anything()
    {
        var res = await factory.CreateApiClient().GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Empty((await res.JsonAsync()).GetProperty("checks").EnumerateArray());
    }

    [Fact]
    public async Task Readiness_lists_each_dependency_without_internal_details()
    {
        var res = await factory.CreateApiClient().GetAsync("/health/ready");

        // Gotenberg fora do ar nos testes: degradado (envio falha), mas a app segue recebendo tráfego.
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == HttpStatusCode.OK, body);
        var checks = (await res.JsonAsync()).GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("status").GetString());
        Assert.Equal("Healthy", checks["database"]);
        Assert.Equal("Degraded", checks["gotenberg"]);
        Assert.Equal("Healthy", checks["storage"]);
        Assert.Equal("Healthy", checks["signature-balance"]); // provedor fake: sem saldo a vigiar
        Assert.DoesNotContain("127.0.0.1", body);
        Assert.DoesNotContain("Exception", body);
    }

    [Fact]
    public async Task Api_responses_are_not_cached_and_isolate_the_browsing_context()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var res = await admin.Client.GetAsync("/api/v1/auth/me");

        Assert.Contains("no-store", res.Headers.CacheControl?.ToString());
        Assert.Equal("same-origin", res.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
    }
}

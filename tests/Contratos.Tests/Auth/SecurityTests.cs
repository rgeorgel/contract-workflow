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
    public async Task Health_reports_database()
    {
        var res = await factory.CreateApiClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}

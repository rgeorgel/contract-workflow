using System.Net;
using Contratos.Tests.Infrastructure;

namespace Contratos.Tests.Auth;

public sealed class RateLimitTests(LowLimitApiFactory factory) : IClassFixture<LowLimitApiFactory>
{
    [Fact]
    public async Task Api_is_rate_limited_per_client_but_static_pages_are_not()
    {
        var client = factory.CreateApiClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 8; i++)
            statuses.Add((await client.GetAsync("/api/v1/auth/me")).StatusCode);

        Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/login.html")).StatusCode);
    }

    [Fact]
    public async Task Public_api_is_limited_per_key_and_per_ip_with_retry_after()
    {
        HttpClient Key(string token)
        {
            var http = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            return http;
        }

        // Por chave: 3 por minuto neste teste; a 4ª espera.
        var one = Key("ctr_chave-inventada-numero-um-0000000000000000000");
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await one.GetAsync("/api/public/v1/account")).StatusCode);
        var limited = await one.GetAsync("/api/public/v1/account");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter is not null);
        Assert.Equal("rate_limited", (await limited.JsonAsync()).GetProperty("code").GetString());

        // Trocar de chave a cada tentativa não escapa: o IP tem um teto (5 vezes o da chave).
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 15; i++)
            statuses.Add((await Key($"ctr_chave-inventada-{i:00}-000000000000000000000000000").GetAsync("/api/public/v1/account")).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}

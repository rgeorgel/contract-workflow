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
}

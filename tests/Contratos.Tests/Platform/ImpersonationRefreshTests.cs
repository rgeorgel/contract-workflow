using System.Net;
using Contratos.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Contratos.Tests.Platform;

/// <summary>Revalida o cookie a cada requisição (em produção, a cada minuto).</summary>
public sealed class EagerStampApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero));
    }
}

public sealed class ImpersonationRefreshTests(EagerStampApiFactory factory) : IClassFixture<EagerStampApiFactory>
{
    [Fact]
    public async Task A_revalidacao_do_cookie_mantem_a_marca_do_entrar_como()
    {
        var sa = await TestAccounts.CreateAdminAsync(factory, "Equipe", ApiFactory.PlatformAdminEmails[0]);
        var customer = await TestAccounts.CreateAdminAsync(factory);
        var me = await (await customer.Client.GetAsync("/api/v1/auth/me")).JsonAsync();
        var orgId = me.GetProperty("organization").GetProperty("id").GetGuid();
        var userId = me.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await sa.Client.PostAsync($"/api/v1/superadmin/tenants/{orgId}/users/{userId}/impersonate")).StatusCode);
        for (var i = 0; i < 3; i++)
        {
            var now = await (await sa.Client.GetAsync("/api/v1/auth/me")).JsonAsync();
            Assert.Equal(userId, now.GetProperty("id").GetGuid());
            Assert.Equal("Admin Teste", now.GetProperty("impersonation").GetProperty("impersonatorName").GetString());
        }

        // A escrita depois da revalidação continua auditada em nome da equipe.
        Assert.Equal(HttpStatusCode.Created, (await sa.Client.PostAsync("/api/v1/templates", new { name = "Depois da revalidação" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sa.Client.PostAsync("/api/v1/auth/stop-impersonation")).StatusCode);
        var detail = await (await sa.Client.GetAsync($"/api/v1/superadmin/tenants/{orgId}")).JsonAsync();
        Assert.Contains(detail.GetProperty("audit").EnumerateArray(), a => a.GetProperty("action").GetString() == "ImpersonatedWrite");
    }
}

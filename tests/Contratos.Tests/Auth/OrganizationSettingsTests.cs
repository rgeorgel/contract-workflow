using System.Net;
using Contratos.Tests.Infrastructure;

namespace Contratos.Tests.Auth;

[Collection(ApiCollection.Name)]
public sealed class OrganizationSettingsTests(ApiFactory factory)
{
    [Fact]
    public async Task Admin_updates_name_and_sending_defaults()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);

        var res = await admin.Client.PutAsync("/api/v1/organization/settings",
            new { name = "Nome Novo Ltda", reminder = "WEEKLY", defaultMessage = "Por favor, assine.", language = "pt-BR" });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var org = await (await admin.Client.GetAsync("/api/v1/organization/settings")).JsonAsync();
        Assert.Equal("Nome Novo Ltda", org.GetProperty("name").GetString());
        Assert.Equal("WEEKLY", org.GetProperty("settings").GetProperty("reminder").GetString());
        Assert.Equal(0, org.GetProperty("usage").GetProperty("contractsSent").GetInt32());
    }

    [Fact]
    public async Task Invalid_values_are_rejected()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);

        var res = await admin.Client.PutAsync("/api/v1/organization/settings", new { name = "", reminder = "HOURLY", language = "xx" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await res.JsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("reminder", out _));
        Assert.True(errors.TryGetProperty("language", out _));
    }

    [Fact]
    public async Task Readers_see_settings_but_cannot_change_them()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var reader = await TestAccounts.InviteAndAcceptAsync(factory, admin, "Leitor");

        Assert.Equal(HttpStatusCode.OK, (await reader.Client.GetAsync("/api/v1/organization/settings")).StatusCode);
        var res = await reader.Client.PutAsync("/api/v1/organization/settings", new { name = "Hack", language = "pt-BR" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}

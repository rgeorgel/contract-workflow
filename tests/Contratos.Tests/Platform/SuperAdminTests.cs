using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Contratos.Tests.Infrastructure;
using Contratos.Tests.Templates;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Contratos.Tests.Platform;

[Collection(ApiCollection.Name)]
public sealed class SuperAdminTests(ApiFactory factory)
{
    private Task<TestAccount> PlatformAdminAsync(int n) =>
        TestAccounts.CreateAdminAsync(factory, "Equipe da plataforma", ApiFactory.PlatformAdminEmails[n - 1]);

    private static async Task<Guid> OrgIdAsync(TestAccount account) =>
        (await (await account.Client.GetAsync("/api/v1/auth/me")).JsonAsync()).GetProperty("organization").GetProperty("id").GetGuid();

    private static async Task<JsonElement> DetailAsync(TestAccount sa, Guid id)
    {
        var res = await sa.Client.GetAsync($"/api/v1/superadmin/tenants/{id}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.JsonAsync();
    }

    private async Task<HttpResponseMessage> LoginAsync(TestAccount account) =>
        await factory.CreateApiClient().PostAsync("/api/v1/auth/login", new { email = account.Email, password = account.Password });

    [Fact]
    public async Task So_a_equipe_da_plataforma_acessa_o_portal()
    {
        var customer = await TestAccounts.CreateAdminAsync(factory);
        Assert.False((await (await customer.Client.GetAsync("/api/v1/auth/me")).JsonAsync()).GetProperty("platformAdmin").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Client.GetAsync("/api/v1/superadmin/tenants")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Client.GetAsync("/api/v1/superadmin/overview")).StatusCode);

        var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/superadmin/tenants")).StatusCode);

        // A chave da API de operação não abre o portal (só o cookie de alguém da equipe).
        var withKey = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        withKey.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.PlatformKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await withKey.GetAsync("/api/v1/superadmin/tenants")).StatusCode);

        var sa = await PlatformAdminAsync(1);
        Assert.True((await (await sa.Client.GetAsync("/api/v1/auth/me")).JsonAsync()).GetProperty("platformAdmin").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await sa.Client.GetAsync("/api/v1/superadmin/overview")).StatusCode);
    }

    [Fact]
    public async Task Lista_e_detalhe_mostram_plano_uso_e_ultimo_acesso()
    {
        var sa = await PlatformAdminAsync(2);
        var customer = await TestAccounts.CreateAdminAsync(factory);
        await TemplatesApiTests.CreatePublishedAsync(customer);
        await TestAccounts.InviteAndAcceptAsync(factory, customer, "Editor");
        var orgId = await OrgIdAsync(customer);

        // Busca pelo e-mail de um usuário acha a organização.
        var list = await (await sa.Client.GetAsync($"/api/v1/superadmin/tenants?q={Uri.EscapeDataString(customer.Email)}")).JsonAsync();
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        var item = list.GetProperty("items")[0];
        Assert.Equal(orgId, item.GetProperty("id").GetGuid());
        Assert.Equal(1, item.GetProperty("usage").GetProperty("templatesUsed").GetInt32());
        Assert.Equal(2, item.GetProperty("usage").GetProperty("usersUsed").GetInt32());
        Assert.Equal(customer.Email, item.GetProperty("owner").GetProperty("email").GetString());
        Assert.NotEqual(JsonValueKind.Null, item.GetProperty("lastSeenAt").ValueKind);

        // Filtros e ordenações respondem (a conta nova está no plano padrão dos testes, Empresa, sem Stripe).
        foreach (var query in new[] { "status=manual", "plan=empresa", "activity=active7", "sort=created", "sort=contracts", "sort=name" })
            Assert.Equal(HttpStatusCode.OK, (await sa.Client.GetAsync($"/api/v1/superadmin/tenants?{query}")).StatusCode);
        var manual = await (await sa.Client.GetAsync($"/api/v1/superadmin/tenants?status=manual&q={Uri.EscapeDataString(customer.Email)}")).JsonAsync();
        Assert.Equal(1, manual.GetProperty("total").GetInt32());

        var detail = await DetailAsync(sa, orgId);
        Assert.Equal(2, detail.GetProperty("users").GetArrayLength());
        Assert.All(detail.GetProperty("users").EnumerateArray(), u => Assert.NotEqual(JsonValueKind.Null, u.GetProperty("lastSeenAt").ValueKind));
        Assert.Equal("Prestação de serviços", detail.GetProperty("templates")[0].GetProperty("name").GetString());

        var timeline = await (await sa.Client.GetAsync($"/api/v1/superadmin/tenants/{orgId}/timeline?days=7")).JsonAsync();
        var types = timeline.EnumerateArray().Select(g => g.GetProperty("type").GetString()).ToList();
        Assert.Contains("OrganizationCreated", types);
        Assert.Contains("UserInvited", types);
        Assert.Contains("TemplatePublished", types);

        Assert.Equal(HttpStatusCode.BadRequest, (await sa.Client.GetAsync($"/api/v1/superadmin/tenants/{orgId}/timeline?category=xyz")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await sa.Client.GetAsync($"/api/v1/superadmin/tenants/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Trocar_plano_e_conceder_creditos_ficam_registrados()
    {
        var sa = await PlatformAdminAsync(3);
        var customer = await TestAccounts.CreateAdminAsync(factory);
        var orgId = await OrgIdAsync(customer);

        var plan = await sa.Client.PutAsync($"/api/v1/superadmin/tenants/{orgId}/plan", new { plan = "essencial" });
        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
        Assert.Equal("essencial", (await plan.JsonAsync()).GetProperty("summary").GetProperty("planId").GetString());

        var invalid = await sa.Client.PostAsync($"/api/v1/superadmin/tenants/{orgId}/credits", new { quantity = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var credits = await sa.Client.PostAsync($"/api/v1/superadmin/tenants/{orgId}/credits", new { quantity = 7, note = "Cortesia" });
        Assert.Equal(HttpStatusCode.OK, credits.StatusCode);
        var detail = await credits.JsonAsync();
        Assert.Equal(7, detail.GetProperty("billing").GetProperty("creditsAvailable").GetInt32());

        var audit = detail.GetProperty("audit").EnumerateArray().ToList();
        Assert.Equal(["CreditsGranted", "PlanChanged"], audit.Select(a => a.GetProperty("action").GetString()));
        Assert.Contains(ApiFactory.PlatformAdminEmails[2], audit[0].GetProperty("actorName").GetString());
        Assert.Contains("Essencial", audit[1].GetProperty("details").GetString());

        var timeline = await (await sa.Client.GetAsync($"/api/v1/superadmin/tenants/{orgId}/timeline?category=superadmin")).JsonAsync();
        Assert.Equal(2, timeline.GetArrayLength());
        Assert.All(timeline.EnumerateArray(), g => Assert.Equal("superadmin", g.GetProperty("category").GetString()));

        // O cliente vê o plano novo.
        var billing = await (await customer.Client.GetAsync("/api/v1/organization/billing")).JsonAsync();
        Assert.Equal("essencial", billing.GetProperty("plan").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Conta_suspensa_nao_entra_ate_ser_reativada()
    {
        var sa = await PlatformAdminAsync(4);
        var customer = await TestAccounts.CreateAdminAsync(factory);
        var orgId = await OrgIdAsync(customer);
        var keyRes = await customer.Client.PostAsync("/api/v1/integrations/keys", new { name = "CRM" });
        var token = (await keyRes.JsonAsync()).GetProperty("token").GetString()!;
        var publicApi = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        publicApi.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await publicApi.GetAsync("/api/public/v1/templates")).StatusCode);

        // Não dá para suspender a própria organização.
        var own = await sa.Client.PostAsync($"/api/v1/superadmin/tenants/{await OrgIdAsync(sa)}/suspend", new { reason = "teste" });
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);

        var suspend = await sa.Client.PostAsync($"/api/v1/superadmin/tenants/{orgId}/suspend", new { reason = "Inadimplência" });
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        var detail = await suspend.JsonAsync();
        Assert.Equal("suspended", detail.GetProperty("summary").GetProperty("status").GetString());
        Assert.Equal("Inadimplência", detail.GetProperty("suspensionReason").GetString());

        // Sessão aberta, novo login e chave de API param de valer.
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.Client.GetAsync("/api/v1/auth/me")).StatusCode);
        var login = await LoginAsync(customer);
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal("account_suspended", (await login.JsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await publicApi.GetAsync("/api/public/v1/templates")).StatusCode);

        var list = await (await sa.Client.GetAsync($"/api/v1/superadmin/tenants?status=suspended&q={Uri.EscapeDataString(customer.Email)}")).JsonAsync();
        Assert.Equal(1, list.GetProperty("total").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await sa.Client.PostAsync($"/api/v1/superadmin/tenants/{orgId}/reactivate")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LoginAsync(customer)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await publicApi.GetAsync("/api/public/v1/templates")).StatusCode);

        var actions = (await DetailAsync(sa, orgId)).GetProperty("audit").EnumerateArray().Select(a => a.GetProperty("action").GetString());
        Assert.Equal(["Reactivated", "Suspended"], actions);
    }

    [Fact]
    public async Task Visao_geral_conta_organizacoes_e_cadastros()
    {
        var sa = await PlatformAdminAsync(5);
        await TestAccounts.CreateAdminAsync(factory);
        var overview = await (await sa.Client.GetAsync("/api/v1/superadmin/overview")).JsonAsync();
        Assert.True(overview.GetProperty("tenants").GetInt32() >= 2);
        Assert.True(overview.GetProperty("newLast7Days").GetInt32() >= 2);
        Assert.Equal(30, overview.GetProperty("signupsByDay").GetArrayLength());
        Assert.True(overview.GetProperty("signupsByDay").EnumerateArray().Last().GetProperty("count").GetInt32() >= 2);
        Assert.True(overview.GetProperty("newest").GetArrayLength() >= 2);
    }
}

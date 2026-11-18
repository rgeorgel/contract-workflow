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

    // ---------- Entrar como ----------

    private static async Task<Guid> UserIdAsync(TestAccount account) =>
        (await (await account.Client.GetAsync("/api/v1/auth/me")).JsonAsync()).GetProperty("id").GetGuid();

    [Fact]
    public async Task Entrar_como_usa_a_conta_do_cliente_e_registra_cada_alteracao()
    {
        var sa = await PlatformAdminAsync(6);
        var customer = await TestAccounts.CreateAdminAsync(factory);
        var orgId = await OrgIdAsync(customer);
        var customerId = await UserIdAsync(customer);
        var detail = await DetailAsync(sa, orgId);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("users")[0].GetProperty("impersonationBlockedReason").ValueKind);

        var start = await sa.Client.PostAsync($"/api/v1/superadmin/tenants/{orgId}/users/{customerId}/impersonate");
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("/dashboard.html", (await start.JsonAsync()).GetProperty("redirect").GetString());

        // A sessão agora é do cliente, sem o acesso ao portal, e sabe quem da equipe está por trás.
        var me = await (await sa.Client.GetAsync("/api/v1/auth/me")).JsonAsync();
        Assert.Equal(customerId, me.GetProperty("id").GetGuid());
        Assert.False(me.GetProperty("platformAdmin").GetBoolean());
        Assert.Equal("Admin Teste", me.GetProperty("impersonation").GetProperty("impersonatorName").GetString());
        var expires = me.GetProperty("impersonation").GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expires, DateTimeOffset.UtcNow.AddMinutes(55), DateTimeOffset.UtcNow.AddMinutes(61));
        Assert.Equal(HttpStatusCode.Forbidden, (await sa.Client.GetAsync("/api/v1/superadmin/overview")).StatusCode);

        // Acesso total: cria um template como o cliente; a escrita (e a recusada) vão para a auditoria.
        Assert.Equal(HttpStatusCode.Created, (await sa.Client.PostAsync("/api/v1/templates", new { name = "Feito pela equipe" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await sa.Client.PostAsync("/api/v1/templates", new { name = "" })).StatusCode);

        var stop = await sa.Client.PostAsync("/api/v1/auth/stop-impersonation");
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        Assert.Equal($"/superadmin/tenant.html?id={orgId}", (await stop.JsonAsync()).GetProperty("redirect").GetString());
        Assert.True((await (await sa.Client.GetAsync("/api/v1/auth/me")).JsonAsync()).GetProperty("platformAdmin").GetBoolean());

        var audit = (await DetailAsync(sa, orgId)).GetProperty("audit").EnumerateArray().ToList();
        Assert.Equal(["ImpersonationEnded", "ImpersonatedWrite", "ImpersonatedWrite", "Impersonated"],
            audit.Select(a => a.GetProperty("action").GetString()));
        Assert.Equal("POST /api/v1/templates → 400", audit[1].GetProperty("details").GetString());
        Assert.Equal("POST /api/v1/templates → 201", audit[2].GetProperty("details").GetString());
        Assert.StartsWith("Admin Teste (como Admin Teste)", audit[2].GetProperty("actorName").GetString());
        Assert.Contains(customer.Email, audit[3].GetProperty("details").GetString());

        // A sessão de volta é a da equipe: sair de novo não faz sentido.
        Assert.Equal(HttpStatusCode.Conflict, (await sa.Client.PostAsync("/api/v1/auth/stop-impersonation")).StatusCode);
    }

    [Fact]
    public async Task Entrar_como_recusa_equipe_desativados_e_contas_suspensas()
    {
        var sa = await PlatformAdminAsync(7);
        var customer = await TestAccounts.CreateAdminAsync(factory);
        var orgId = await OrgIdAsync(customer);
        var editor = await TestAccounts.InviteAndAcceptAsync(factory, customer, "Editor");
        var editorId = await UserIdAsync(editor);

        // Clientes não usam a rota.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await customer.Client.PostAsync($"/api/v1/superadmin/tenants/{orgId}/users/{editorId}/impersonate")).StatusCode);

        // Usuário de outra organização, alguém da equipe e a própria conta: não.
        Assert.Equal(HttpStatusCode.NotFound,
            (await sa.Client.PostAsync($"/api/v1/superadmin/tenants/{await OrgIdAsync(sa)}/users/{editorId}/impersonate")).StatusCode);
        var self = await sa.Client.PostAsync($"/api/v1/superadmin/tenants/{await OrgIdAsync(sa)}/users/{await UserIdAsync(sa)}/impersonate");
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal("cannot_impersonate", (await self.JsonAsync()).GetProperty("code").GetString());
        var colleague = await PlatformAdminAsync(8);
        var colleagueRes = await sa.Client.PostAsync($"/api/v1/superadmin/tenants/{await OrgIdAsync(colleague)}/users/{await UserIdAsync(colleague)}/impersonate");
        Assert.Equal(HttpStatusCode.Conflict, colleagueRes.StatusCode);

        // Desativado.
        Assert.Equal(HttpStatusCode.OK, (await customer.Client.PutAsync($"/api/v1/users/{editorId}", new { active = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await sa.Client.PostAsync($"/api/v1/superadmin/tenants/{orgId}/users/{editorId}/impersonate")).StatusCode);
        var users = (await DetailAsync(sa, orgId)).GetProperty("users").EnumerateArray().ToList();
        Assert.Contains(users, u => u.GetProperty("id").GetGuid() == editorId && u.GetProperty("impersonationBlockedReason").GetString() == "o usuário está desativado");

        // Organização suspensa.
        Assert.Equal(HttpStatusCode.OK, (await sa.Client.PostAsync($"/api/v1/superadmin/tenants/{orgId}/suspend", new { reason = "teste" })).StatusCode);
        var customerId = users.Single(u => u.GetProperty("role").GetString() == "Admin").GetProperty("id").GetGuid();
        var suspended = await sa.Client.PostAsync($"/api/v1/superadmin/tenants/{orgId}/users/{customerId}/impersonate");
        Assert.Equal(HttpStatusCode.Conflict, suspended.StatusCode);
        Assert.Contains("suspensa", (await suspended.JsonAsync()).GetProperty("title").GetString());
    }
}

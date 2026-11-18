using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Contratos.Infrastructure.Platform;
using Contratos.Tests.Infrastructure;
using Contratos.Tests.Templates;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Contratos.Tests.Platform;

[Collection(ApiCollection.Name)]
public sealed class SuperAdminTests(ApiFactory factory)
{
    private const string Api = "/superadmin/api/v1";

    private static async Task<Guid> OrgIdAsync(TestAccount account) =>
        (await (await account.Client.GetAsync("/api/v1/auth/me")).JsonAsync()).GetProperty("organization").GetProperty("id").GetGuid();

    private static async Task<Guid> UserIdAsync(TestAccount account) =>
        (await (await account.Client.GetAsync("/api/v1/auth/me")).JsonAsync()).GetProperty("id").GetGuid();

    private static async Task<JsonElement> DetailAsync(ApiClient sa, Guid id)
    {
        var res = await sa.GetAsync($"{Api}/tenants/{id}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.JsonAsync();
    }

    private static Task<HttpResponseMessage> AdminLoginAsync(ApiClient client, string email, string password) =>
        client.PostAsync($"{Api}/auth/login", new { email, password });

    private async Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        await factory.CreateApiClient().PostAsync("/api/v1/auth/login", new { email, password });

    // ---------- Contas e sessões separadas ----------

    [Fact]
    public async Task Portal_e_app_tem_logins_e_cookies_separados()
    {
        // Cliente logado no app (Admin da organização) não abre o portal.
        var customer = await TestAccounts.CreateAdminAsync(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.Client.GetAsync($"{Api}/tenants")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.Client.GetAsync($"{Api}/auth/me")).StatusCode);
        // E as credenciais do app não valem no login do portal.
        Assert.Equal(HttpStatusCode.Unauthorized, (await AdminLoginAsync(factory.CreateApiClient(), customer.Email, customer.Password)).StatusCode);

        // A chave da API de operação também não abre o portal.
        var withKey = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        withKey.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.PlatformKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await withKey.GetAsync($"{Api}/tenants")).StatusCode);

        // Conta do portal: entra no portal, mas não é usuário do app.
        var (sa, email) = await factory.CreatePlatformAdminAsync("Marta Equipe");
        var me = await (await sa.GetAsync($"{Api}/auth/me")).JsonAsync();
        Assert.Equal(email, me.GetProperty("email").GetString());
        Assert.Equal("Marta Equipe", me.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.OK, (await sa.GetAsync($"{Api}/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await sa.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, ApiFactory.AdminPassword)).StatusCode);

        // O cookie do portal só vai para /superadmin.
        var cookie = (await AdminLoginAsync(factory.CreateApiClient(), email, ApiFactory.AdminPassword)).Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith("contratos.admin=", StringComparison.Ordinal));
        Assert.Contains("path=/superadmin", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.NoContent, (await sa.PostAsync($"{Api}/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await sa.GetAsync($"{Api}/overview")).StatusCode);
    }

    [Fact]
    public async Task Login_do_portal_bloqueia_depois_de_tentativas_erradas()
    {
        var (_, email) = await factory.CreatePlatformAdminAsync();
        var client = factory.CreateApiClient();
        for (var i = 1; i < PlatformAdminService.MaxFailedLogins; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await AdminLoginAsync(client, email, "senha-errada-123")).StatusCode);
        Assert.Equal(HttpStatusCode.Locked, (await AdminLoginAsync(client, email, "senha-errada-123")).StatusCode);
        // Bloqueada: nem a senha certa entra até o prazo passar.
        Assert.Equal(HttpStatusCode.Locked, (await AdminLoginAsync(client, email, ApiFactory.AdminPassword)).StatusCode);
        // E-mail desconhecido responde igual a senha errada.
        Assert.Equal(HttpStatusCode.Unauthorized, (await AdminLoginAsync(client, "ninguem@plataforma.test", "qualquer-senha-123")).StatusCode);
    }

    [Fact]
    public async Task Desativar_ou_trocar_a_senha_derruba_a_sessao_do_portal()
    {
        var (sa, email) = await factory.CreatePlatformAdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await sa.GetAsync($"{Api}/overview")).StatusCode);

        using (var scope = factory.Services.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<PlatformAdminService>().SetPasswordAsync(email, "outra-senha-bem-longa", default)).IsSuccess);
        Assert.Equal(HttpStatusCode.Unauthorized, (await sa.GetAsync($"{Api}/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await AdminLoginAsync(sa, email, "outra-senha-bem-longa")).StatusCode);

        using (var scope = factory.Services.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<PlatformAdminService>().SetActiveAsync(email, false, default)).IsSuccess);
        Assert.Equal(HttpStatusCode.Unauthorized, (await sa.GetAsync($"{Api}/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AdminLoginAsync(sa, email, "outra-senha-bem-longa")).StatusCode);
    }

    [Fact]
    public async Task Contas_do_portal_exigem_senha_forte_e_email_unico()
    {
        using var scope = factory.Services.CreateScope();
        var admins = scope.ServiceProvider.GetRequiredService<PlatformAdminService>();
        Assert.False((await admins.CreateAsync("curta@plataforma.test", "Curta", "123", default)).IsSuccess);
        Assert.False((await admins.CreateAsync("não é e-mail", "X", "senha-bem-longa-ok", default)).IsSuccess);
        var email = $"dup-{Guid.NewGuid():N}@plataforma.test";
        Assert.True((await admins.CreateAsync(email, "Um", "senha-bem-longa-ok", default)).IsSuccess);
        var dup = await admins.CreateAsync(email.ToUpperInvariant(), "Dois", "senha-bem-longa-ok", default);
        Assert.Equal("email_taken", dup.Error!.Code);
    }

    // ---------- Organizações ----------

    [Fact]
    public async Task Lista_e_detalhe_mostram_plano_uso_e_ultimo_acesso()
    {
        var (sa, _) = await factory.CreatePlatformAdminAsync();
        var customer = await TestAccounts.CreateAdminAsync(factory);
        await TemplatesApiTests.CreatePublishedAsync(customer);
        await TestAccounts.InviteAndAcceptAsync(factory, customer, "Editor");
        var orgId = await OrgIdAsync(customer);

        var list = await (await sa.GetAsync($"{Api}/tenants?q={Uri.EscapeDataString(customer.Email)}")).JsonAsync();
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        var item = list.GetProperty("items")[0];
        Assert.Equal(orgId, item.GetProperty("id").GetGuid());
        Assert.Equal(1, item.GetProperty("usage").GetProperty("templatesUsed").GetInt32());
        Assert.Equal(2, item.GetProperty("usage").GetProperty("usersUsed").GetInt32());
        Assert.Equal(customer.Email, item.GetProperty("owner").GetProperty("email").GetString());
        Assert.NotEqual(JsonValueKind.Null, item.GetProperty("lastSeenAt").ValueKind);

        foreach (var query in new[] { "status=manual", "plan=empresa", "activity=active7", "sort=created", "sort=contracts", "sort=name" })
            Assert.Equal(HttpStatusCode.OK, (await sa.GetAsync($"{Api}/tenants?{query}")).StatusCode);
        var manual = await (await sa.GetAsync($"{Api}/tenants?status=manual&q={Uri.EscapeDataString(customer.Email)}")).JsonAsync();
        Assert.Equal(1, manual.GetProperty("total").GetInt32());

        var detail = await DetailAsync(sa, orgId);
        Assert.Equal(2, detail.GetProperty("users").GetArrayLength());
        Assert.All(detail.GetProperty("users").EnumerateArray(), u => Assert.NotEqual(JsonValueKind.Null, u.GetProperty("lastSeenAt").ValueKind));
        Assert.Equal("Prestação de serviços", detail.GetProperty("templates")[0].GetProperty("name").GetString());

        var timeline = await (await sa.GetAsync($"{Api}/tenants/{orgId}/timeline?days=7")).JsonAsync();
        var types = timeline.EnumerateArray().Select(g => g.GetProperty("type").GetString()).ToList();
        Assert.Contains("OrganizationCreated", types);
        Assert.Contains("UserInvited", types);
        Assert.Contains("TemplatePublished", types);

        Assert.Equal(HttpStatusCode.BadRequest, (await sa.GetAsync($"{Api}/tenants/{orgId}/timeline?category=xyz")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await sa.GetAsync($"{Api}/tenants/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Trocar_plano_e_conceder_creditos_ficam_registrados()
    {
        var (sa, email) = await factory.CreatePlatformAdminAsync();
        var customer = await TestAccounts.CreateAdminAsync(factory);
        var orgId = await OrgIdAsync(customer);

        var plan = await sa.PutAsync($"{Api}/tenants/{orgId}/plan", new { plan = "essencial" });
        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
        Assert.Equal("essencial", (await plan.JsonAsync()).GetProperty("summary").GetProperty("planId").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await sa.PostAsync($"{Api}/tenants/{orgId}/credits", new { quantity = 0 })).StatusCode);

        var credits = await sa.PostAsync($"{Api}/tenants/{orgId}/credits", new { quantity = 7, note = "Cortesia" });
        Assert.Equal(HttpStatusCode.OK, credits.StatusCode);
        var detail = await credits.JsonAsync();
        Assert.Equal(7, detail.GetProperty("billing").GetProperty("creditsAvailable").GetInt32());

        var audit = detail.GetProperty("audit").EnumerateArray().ToList();
        Assert.Equal(["CreditsGranted", "PlanChanged"], audit.Select(a => a.GetProperty("action").GetString()));
        Assert.Contains(email, audit[0].GetProperty("actorName").GetString());
        Assert.Contains("Essencial", audit[1].GetProperty("details").GetString());

        var timeline = await (await sa.GetAsync($"{Api}/tenants/{orgId}/timeline?category=superadmin")).JsonAsync();
        Assert.Equal(2, timeline.GetArrayLength());

        var billing = await (await customer.Client.GetAsync("/api/v1/organization/billing")).JsonAsync();
        Assert.Equal("essencial", billing.GetProperty("plan").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Conta_suspensa_nao_entra_ate_ser_reativada()
    {
        var (sa, _) = await factory.CreatePlatformAdminAsync();
        var customer = await TestAccounts.CreateAdminAsync(factory);
        var orgId = await OrgIdAsync(customer);
        var token = (await (await customer.Client.PostAsync("/api/v1/integrations/keys", new { name = "CRM" })).JsonAsync()).GetProperty("token").GetString()!;
        var publicApi = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        publicApi.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await publicApi.GetAsync("/api/public/v1/templates")).StatusCode);

        var suspend = await sa.PostAsync($"{Api}/tenants/{orgId}/suspend", new { reason = "Inadimplência" });
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        var detail = await suspend.JsonAsync();
        Assert.Equal("suspended", detail.GetProperty("summary").GetProperty("status").GetString());
        Assert.Equal("Inadimplência", detail.GetProperty("suspensionReason").GetString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.Client.GetAsync("/api/v1/auth/me")).StatusCode);
        var login = await LoginAsync(customer.Email, customer.Password);
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal("account_suspended", (await login.JsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await publicApi.GetAsync("/api/public/v1/templates")).StatusCode);

        var list = await (await sa.GetAsync($"{Api}/tenants?status=suspended&q={Uri.EscapeDataString(customer.Email)}")).JsonAsync();
        Assert.Equal(1, list.GetProperty("total").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await sa.PostAsync($"{Api}/tenants/{orgId}/reactivate")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LoginAsync(customer.Email, customer.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await publicApi.GetAsync("/api/public/v1/templates")).StatusCode);

        var actions = (await DetailAsync(sa, orgId)).GetProperty("audit").EnumerateArray().Select(a => a.GetProperty("action").GetString());
        Assert.Equal(["Reactivated", "Suspended"], actions);
    }

    [Fact]
    public async Task Visao_geral_conta_organizacoes_e_cadastros()
    {
        var (sa, _) = await factory.CreatePlatformAdminAsync();
        await TestAccounts.CreateAdminAsync(factory);
        await TestAccounts.CreateAdminAsync(factory);
        var overview = await (await sa.GetAsync($"{Api}/overview")).JsonAsync();
        Assert.True(overview.GetProperty("tenants").GetInt32() >= 2);
        Assert.True(overview.GetProperty("newLast7Days").GetInt32() >= 2);
        Assert.Equal(30, overview.GetProperty("signupsByDay").GetArrayLength());
        Assert.True(overview.GetProperty("newest").GetArrayLength() >= 2);
    }

    // ---------- Entrar como ----------

    [Fact]
    public async Task Entrar_como_abre_sessao_no_app_e_registra_cada_alteracao()
    {
        var (sa, email) = await factory.CreatePlatformAdminAsync("Marta Equipe");
        var customer = await TestAccounts.CreateAdminAsync(factory);
        var orgId = await OrgIdAsync(customer);
        var customerId = await UserIdAsync(customer);
        Assert.Equal(JsonValueKind.Null, (await DetailAsync(sa, orgId)).GetProperty("users")[0].GetProperty("impersonationBlockedReason").ValueKind);

        var start = await sa.PostAsync($"{Api}/tenants/{orgId}/users/{customerId}/impersonate");
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal("/dashboard.html", (await start.JsonAsync()).GetProperty("redirect").GetString());

        // O mesmo navegador agora tem as duas sessões: a do app (como o cliente) e a do portal, intacta.
        var me = await (await sa.GetAsync("/api/v1/auth/me")).JsonAsync();
        Assert.Equal(customerId, me.GetProperty("id").GetGuid());
        Assert.Equal("Marta Equipe", me.GetProperty("impersonation").GetProperty("impersonatorName").GetString());
        Assert.InRange(me.GetProperty("impersonation").GetProperty("expiresAt").GetDateTimeOffset(),
            DateTimeOffset.UtcNow.AddMinutes(55), DateTimeOffset.UtcNow.AddMinutes(61));
        Assert.False(me.TryGetProperty("platformAdmin", out _));
        Assert.Equal(HttpStatusCode.OK, (await sa.GetAsync($"{Api}/overview")).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await sa.PostAsync("/api/v1/templates", new { name = "Feito pela equipe" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await sa.PostAsync("/api/v1/templates", new { name = "" })).StatusCode);

        // Encerrar sai do app (vai para o login do app); o portal continua logado.
        var stop = await sa.PostAsync("/api/v1/auth/stop-impersonation");
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        Assert.Equal("/login.html", (await stop.JsonAsync()).GetProperty("redirect").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await sa.GetAsync("/api/v1/auth/me")).StatusCode);

        var audit = (await DetailAsync(sa, orgId)).GetProperty("audit").EnumerateArray().ToList();
        Assert.Equal(["ImpersonationEnded", "ImpersonatedWrite", "ImpersonatedWrite", "Impersonated"],
            audit.Select(a => a.GetProperty("action").GetString()));
        Assert.Equal("POST /api/v1/templates → 400", audit[1].GetProperty("details").GetString());
        Assert.Equal("POST /api/v1/templates → 201", audit[2].GetProperty("details").GetString());
        Assert.StartsWith("Marta Equipe (como Admin Teste)", audit[2].GetProperty("actorName").GetString());
        Assert.Contains(email, audit[3].GetProperty("actorName").GetString());
        Assert.Contains(customer.Email, audit[3].GetProperty("details").GetString());

        // Sessão comum do cliente: não há o que encerrar.
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Client.PostAsync("/api/v1/auth/stop-impersonation")).StatusCode);
    }

    [Fact]
    public async Task Entrar_como_recusa_desativados_contas_suspensas_e_clientes()
    {
        var (sa, _) = await factory.CreatePlatformAdminAsync();
        var customer = await TestAccounts.CreateAdminAsync(factory);
        var orgId = await OrgIdAsync(customer);
        var customerId = await UserIdAsync(customer);
        var editor = await TestAccounts.InviteAndAcceptAsync(factory, customer, "Editor");
        var editorId = await UserIdAsync(editor);

        // Clientes não usam a rota (nem o Admin da própria organização).
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await customer.Client.PostAsync($"{Api}/tenants/{orgId}/users/{editorId}/impersonate")).StatusCode);

        // Usuário de outra organização.
        var other = await TestAccounts.CreateAdminAsync(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await sa.PostAsync($"{Api}/tenants/{await OrgIdAsync(other)}/users/{editorId}/impersonate")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await customer.Client.PutAsync($"/api/v1/users/{editorId}", new { active = false })).StatusCode);
        var deactivated = await sa.PostAsync($"{Api}/tenants/{orgId}/users/{editorId}/impersonate");
        Assert.Equal(HttpStatusCode.Conflict, deactivated.StatusCode);
        Assert.Equal("cannot_impersonate", (await deactivated.JsonAsync()).GetProperty("code").GetString());
        var users = (await DetailAsync(sa, orgId)).GetProperty("users").EnumerateArray().ToList();
        Assert.Contains(users, u => u.GetProperty("id").GetGuid() == editorId && u.GetProperty("impersonationBlockedReason").GetString() == "o usuário está desativado");

        Assert.Equal(HttpStatusCode.OK, (await sa.PostAsync($"{Api}/tenants/{orgId}/suspend", new { reason = "teste" })).StatusCode);
        var suspended = await sa.PostAsync($"{Api}/tenants/{orgId}/users/{customerId}/impersonate");
        Assert.Equal(HttpStatusCode.Conflict, suspended.StatusCode);
        Assert.Contains("suspensa", (await suspended.JsonAsync()).GetProperty("title").GetString());
    }
}

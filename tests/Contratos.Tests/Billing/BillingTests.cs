using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Contratos.Infrastructure.Billing;
using Contratos.Infrastructure.Persistence;
using Contratos.Tests.Infrastructure;
using Contratos.Tests.Templates;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contratos.Tests.Billing;

[Collection(ApiCollection.Name)]
public sealed class BillingTests(ApiFactory factory)
{
    private static readonly object ValidValues = new Dictionary<string, string>
    {
        ["contratante.nome"] = "Café Aurora Ltda",
        ["contratante.cpf"] = "529.982.247-25",
        ["valor"] = "1500,50",
    };

    // ---------- Apoio ----------

    private HttpClient Platform(string? key = ApiFactory.PlatformKey)
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (key is not null)
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return http;
    }

    private static async Task<string> SlugAsync(TestAccount admin) =>
        (await (await admin.Client.GetAsync("/api/v1/organization/settings")).JsonAsync()).GetProperty("slug").GetString()!;

    private async Task SetPlanAsync(TestAccount admin, string plan, DateTimeOffset? start = null, DateTimeOffset? end = null)
    {
        var res = await Platform().PutAsJsonAsync($"/api/platform/organizations/{await SlugAsync(admin)}/plan",
            new { plan, periodStart = start, periodEnd = end });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    private async Task<JsonElement> GrantAsync(TestAccount admin, int quantity, string? reference = null)
    {
        var res = await Platform().PostAsJsonAsync($"/api/platform/organizations/{await SlugAsync(admin)}/credits",
            new { quantity, reference, note = "Pacote de teste" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.JsonAsync();
    }

    private static async Task<JsonElement> BillingAsync(TestAccount admin) =>
        await (await admin.Client.GetAsync("/api/v1/organization/billing")).JsonAsync();

    /// <summary>Organização no plano dado, com um template publicado.</summary>
    private async Task<(TestAccount Admin, Guid TemplateId)> AccountAsync(string plan)
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var templateId = (await TemplatesApiTests.CreatePublishedAsync(admin)).GetProperty("id").GetGuid();
        await SetPlanAsync(admin, plan);
        return (admin, templateId);
    }

    private static async Task<Guid> DraftAsync(TestAccount admin, Guid templateId, string title = "Consultoria", string channel = "Email")
    {
        var created = await admin.Client.PostAsync("/api/v1/contracts", new { templateId, title });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.JsonAsync()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PutAsync($"/api/v1/contracts/{id}",
            new { title, variableValues = ValidValues, signingMode = "Parallel" })).StatusCode);
        object signer = channel == "Email"
            ? new { name = "Marina Costa", email = "marina@cliente.com.br", deliveryMethod = "Email" }
            : new { name = "Marina Costa", phone = "+55 11 98765-4321", deliveryMethod = channel };
        var added = await admin.Client.PostAsync($"/api/v1/contracts/{id}/signers", signer);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        return id;
    }

    private static Task<HttpResponseMessage> SendAsync(TestAccount admin, Guid id) =>
        admin.Client.PostAsync($"/api/v1/contracts/{id}/send");

    private static async Task SendOkAsync(TestAccount admin, Guid templateId, int count)
    {
        for (var i = 0; i < count; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await SendAsync(admin, await DraftAsync(admin, templateId))).StatusCode);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage res) =>
        (await res.JsonAsync()).GetProperty("code").GetString();

    // ---------- Catálogo e plano padrão ----------

    [Fact]
    public async Task New_account_gets_the_configured_default_plan_and_the_catalog()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var billing = await BillingAsync(admin);

        Assert.Equal("empresa", billing.GetProperty("plan").GetProperty("id").GetString()); // Billing:DefaultPlan dos testes
        Assert.Equal(["avulso", "essencial", "profissional", "empresa"],
            billing.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("id").GetString()!).ToArray());
        Assert.Equal([1, 5, 10, 25, 50], billing.GetProperty("packs").EnumerateArray().Select(p => p.GetProperty("quantity").GetInt32()).ToArray());
        Assert.Equal(1, billing.GetProperty("usersUsed").GetInt32());
        Assert.Equal(JsonValueKind.Null, billing.GetProperty("usersLimit").ValueKind);
    }

    [Fact]
    public async Task Public_catalog_is_open_and_matches_the_plans_in_code()
    {
        var res = await factory.CreateApiClient().GetAsync("/api/v1/plans"); // sem login
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("max-age=300", res.Headers.CacheControl?.ToString());
        var catalog = await res.JsonAsync();
        var plans = catalog.GetProperty("plans").EnumerateArray().ToList();
        Assert.Equal(Contratos.Domain.Billing.Plans.All.Select(p => p.Id), plans.Select(p => p.GetProperty("id").GetString()));
        var pro = plans.Single(p => p.GetProperty("id").GetString() == "profissional");
        Assert.Equal(89.90m, pro.GetProperty("monthlyPrice").GetDecimal());
        Assert.Equal(80, pro.GetProperty("contractsPerPeriod").GetInt32());
        Assert.Equal(["Email", "WhatsApp", "Sms"], pro.GetProperty("channels").EnumerateArray().Select(c => c.GetString()!).ToArray());
        Assert.Equal(12, catalog.GetProperty("creditValidityMonths").GetInt32());
        Assert.Equal(3, catalog.GetProperty("welcomeCredits").GetInt32());
        Assert.Equal(30, catalog.GetProperty("welcomeCreditDays").GetInt32());
        var avulso = plans[0];
        Assert.Equal(0, avulso.GetProperty("contractsPerPeriod").GetInt32());
        Assert.Equal(["Email", "WhatsApp", "Sms"], avulso.GetProperty("channels").EnumerateArray().Select(c => c.GetString()!).ToArray());
        Assert.Equal(2.40m, catalog.GetProperty("packs").EnumerateArray().Last().GetProperty("unitPrice").GetDecimal());
    }

    // ---------- Cota e créditos ----------

    /// <summary>Organização no plano, já sem nenhum crédito e com um template publicado.</summary>
    private Task<(TestAccount Admin, Guid TemplateId)> AvulsoAsync() => AccountAsync("avulso");

    [Fact]
    public async Task Avulso_sends_only_with_credits()
    {
        var (admin, templateId) = await AvulsoAsync();
        var billing = await BillingAsync(admin);
        Assert.Equal(0, billing.GetProperty("contractsLimit").GetInt32());
        Assert.Equal(0, billing.GetProperty("availableSends").GetInt32());

        var blocked = await DraftAsync(admin, templateId);
        var res = await SendAsync(admin, blocked);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var problem = await res.JsonAsync();
        Assert.Equal("quota_exceeded", problem.GetProperty("code").GetString());
        Assert.Contains("não tem créditos de contrato", problem.GetProperty("title").GetString());
        var contract = await (await admin.Client.GetAsync($"/api/v1/contracts/{blocked}")).JsonAsync();
        Assert.Equal("Draft", contract.GetProperty("status").GetString());
        Assert.False(contract.GetProperty("hasDocument").GetBoolean());

        await GrantAsync(admin, 2);
        await SendOkAsync(admin, templateId, 1);
        Assert.Equal(HttpStatusCode.Accepted, (await SendAsync(admin, blocked)).StatusCode);
        billing = await BillingAsync(admin);
        Assert.Equal(2, billing.GetProperty("contractsFromCredits").GetInt32());
        Assert.Equal(0, billing.GetProperty("creditsAvailable").GetInt32());
    }

    [Fact]
    public async Task Plan_quota_is_used_before_credits_and_credits_after_it_runs_out()
    {
        var (admin, templateId) = await AccountAsync("essencial"); // 20 por mês
        await GrantAsync(admin, 2);

        await SendOkAsync(admin, templateId, 20);
        var billing = await BillingAsync(admin);
        Assert.Equal(20, billing.GetProperty("contractsUsed").GetInt32());
        Assert.Equal(2, billing.GetProperty("creditsAvailable").GetInt32()); // cota do plano primeiro

        await SendOkAsync(admin, templateId, 2);
        billing = await BillingAsync(admin);
        Assert.Equal(20, billing.GetProperty("contractsUsed").GetInt32());
        Assert.Equal(2, billing.GetProperty("contractsFromCredits").GetInt32());
        Assert.Equal(0, billing.GetProperty("availableSends").GetInt32());

        var res = await SendAsync(admin, await DraftAsync(admin, templateId));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("contratos do plano Essencial", (await res.JsonAsync()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Credits_that_expire_first_are_used_first()
    {
        var (admin, templateId) = await AvulsoAsync();
        await GrantAsync(admin, 5);
        await GrantAsync(admin, 3);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var slug = await SlugAsync(admin);
            var orgId = await db.Organizations.Where(o => o.Slug == slug).Select(o => o.Id).SingleAsync();
            var three = await db.CreditGrants.SingleAsync(g => g.OrganizationId == orgId && g.Quantity == 3);
            three.ExpiresAt = DateTimeOffset.UtcNow.AddDays(10); // vence antes do de 5
            await db.SaveChangesAsync();
        }

        await SendOkAsync(admin, templateId, 1);
        var credits = (await BillingAsync(admin)).GetProperty("credits").EnumerateArray()
            .ToDictionary(g => g.GetProperty("quantity").GetInt32(), g => g.GetProperty("remaining").GetInt32());
        Assert.Equal(2, credits[3]);
        Assert.Equal(5, credits[5]);
    }

    [Fact]
    public async Task Expired_credits_do_not_count()
    {
        var (admin, templateId) = await AvulsoAsync();
        await GrantAsync(admin, 2);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var slug = await SlugAsync(admin);
            var orgId = await db.Organizations.Where(o => o.Slug == slug).Select(o => o.Id).SingleAsync();
            await db.CreditGrants.Where(g => g.OrganizationId == orgId)
                .ExecuteUpdateAsync(u => u.SetProperty(g => g.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        Assert.Equal(0, (await BillingAsync(admin)).GetProperty("creditsAvailable").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(admin, await DraftAsync(admin, templateId))).StatusCode);
    }

    [Fact]
    public async Task Two_sends_at_the_same_time_cannot_take_the_last_credit_twice()
    {
        var (admin, templateId) = await AvulsoAsync();
        await GrantAsync(admin, 1);
        var a = await DraftAsync(admin, templateId, "Contrato A");
        var b = await DraftAsync(admin, templateId, "Contrato B");

        var results = await Task.WhenAll(SendAsync(admin, a), SendAsync(admin, b));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Accepted);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, (await BillingAsync(admin)).GetProperty("contractsFromCredits").GetInt32());
    }

    [Fact]
    public async Task Undoing_a_send_before_the_provider_gives_the_credit_back()
    {
        var (admin, templateId) = await AvulsoAsync();
        await GrantAsync(admin, 1);

        // Recusado pelo provedor (dado inválido): volta para rascunho e devolve o crédito.
        var rejected = await DraftAsync(admin, templateId, $"Contrato {Contratos.Infrastructure.Signatures.FakeSignatureProvider.RejectMarker}");
        Assert.Equal(HttpStatusCode.Accepted, (await SendAsync(admin, rejected)).StatusCode);
        Assert.Equal(0, (await BillingAsync(admin)).GetProperty("creditsAvailable").GetInt32());
        await factory.ProcessBackgroundAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/v1/contracts/{rejected}/return-to-draft")).StatusCode);
        var billing = await BillingAsync(admin);
        Assert.Equal(1, billing.GetProperty("creditsAvailable").GetInt32());
        Assert.Equal(0, billing.GetProperty("contractsFromCredits").GetInt32());

        // Cancelado antes de o worker entregar: também não conta.
        var cancelled = await DraftAsync(admin, templateId);
        Assert.Equal(HttpStatusCode.Accepted, (await SendAsync(admin, cancelled)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/v1/contracts/{cancelled}/cancel")).StatusCode);
        Assert.Equal(1, (await BillingAsync(admin)).GetProperty("creditsAvailable").GetInt32());
    }

    [Fact]
    public async Task A_send_that_reached_the_provider_counts_even_if_cancelled()
    {
        var (admin, templateId) = await AvulsoAsync();
        await GrantAsync(admin, 1);
        var id = await DraftAsync(admin, templateId);
        Assert.Equal(HttpStatusCode.Accepted, (await SendAsync(admin, id)).StatusCode);
        await factory.ProcessBackgroundAsync(); // chega ao provedor
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/v1/contracts/{id}/cancel")).StatusCode);
        var billing = await BillingAsync(admin);
        Assert.Equal(0, billing.GetProperty("creditsAvailable").GetInt32());
        Assert.Equal(1, billing.GetProperty("contractsFromCredits").GetInt32());
    }

    [Fact]
    public async Task Custom_billing_period_defines_the_quota_window()
    {
        var (admin, templateId) = await AccountAsync("essencial");
        await SendOkAsync(admin, templateId, 1);
        Assert.Equal(1, (await BillingAsync(admin)).GetProperty("contractsUsed").GetInt32());

        // Novo ciclo pago começando agora: a cota renova.
        var start = DateTimeOffset.UtcNow.AddSeconds(1);
        await SetPlanAsync(admin, "essencial", start, start.AddMonths(1));
        await Task.Delay(1100);
        var billing = await BillingAsync(admin);
        Assert.Equal(0, billing.GetProperty("contractsUsed").GetInt32());
        Assert.Equal(start.ToUnixTimeSeconds(), billing.GetProperty("periodStart").GetDateTimeOffset().ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Admins_are_warned_at_80_and_100_percent_of_the_plan_quota()
    {
        var (admin, templateId) = await AccountAsync("essencial"); // 20: avisos em 16 e 20
        await SendOkAsync(admin, templateId, 15);
        await factory.ProcessBackgroundAsync();
        Assert.DoesNotContain(factory.Emails.To(admin.Email), m => m.Subject.Contains("plano Essencial", StringComparison.Ordinal));

        await SendOkAsync(admin, templateId, 1);
        await factory.ProcessBackgroundAsync();
        Assert.Equal("Você já usou 80% dos contratos do plano Essencial",
            Assert.Single(factory.Emails.To(admin.Email), m => m.Subject.Contains("plano Essencial", StringComparison.Ordinal)).Subject);

        await SendOkAsync(admin, templateId, 4);
        await factory.ProcessBackgroundAsync();
        var full = factory.Emails.To(admin.Email).Last(m => m.Subject.Contains("plano Essencial", StringComparison.Ordinal));
        Assert.Equal("Os contratos do plano Essencial acabaram neste ciclo", full.Subject);
        Assert.Contains("Compre um pacote", full.TextBody);
        Assert.Contains("https://app.test/settings.html", full.TextBody);
    }

    [Fact]
    public async Task Avulso_admins_are_warned_when_one_credit_is_left_and_when_they_run_out()
    {
        var (admin, templateId) = await AvulsoAsync();
        await GrantAsync(admin, 3);
        bool IsCreditAlert(Contratos.Application.Email.EmailMessage m) =>
            m.Subject.StartsWith("Resta 1 contrato", StringComparison.Ordinal) || m.Subject.StartsWith("Os créditos de contrato", StringComparison.Ordinal);

        await SendOkAsync(admin, templateId, 1);
        await factory.ProcessBackgroundAsync();
        Assert.DoesNotContain(factory.Emails.To(admin.Email), IsCreditAlert);

        await SendOkAsync(admin, templateId, 1);
        await factory.ProcessBackgroundAsync();
        Assert.StartsWith("Resta 1 contrato", Assert.Single(factory.Emails.To(admin.Email), IsCreditAlert).Subject);

        await SendOkAsync(admin, templateId, 1);
        await factory.ProcessBackgroundAsync();
        var gone = factory.Emails.To(admin.Email).Where(IsCreditAlert).Last();
        Assert.StartsWith("Os créditos de contrato", gone.Subject);
        Assert.Contains("https://app.test/settings.html#pacotes", gone.TextBody);
    }

    [Theory]
    [InlineData(3, -1)]
    [InlineData(20, 16)]
    [InlineData(80, 64)]
    [InlineData(250, 200)]
    public void Warning_fires_at_80_percent_unless_it_rounds_to_the_whole_quota(int limit, int expected) =>
        Assert.Equal(expected, UsageMeter.WarningAt(limit));

    // ---------- Canais, usuários e templates ----------

    [Fact]
    public async Task Channels_outside_the_plan_are_refused()
    {
        var (admin, templateId) = await AccountAsync("empresa");
        var withSms = await DraftAsync(admin, templateId, channel: "Sms");

        await SetPlanAsync(admin, "essencial"); // e-mail e WhatsApp
        var created = await admin.Client.PostAsync("/api/v1/contracts", new { templateId, title = "Outro" });
        var id = (await created.JsonAsync()).GetProperty("id").GetGuid();
        var add = await admin.Client.PostAsync($"/api/v1/contracts/{id}/signers",
            new { name = "João", phone = "+55 11 98765-4321", deliveryMethod = "Sms" });
        Assert.Equal(HttpStatusCode.BadRequest, add.StatusCode);
        Assert.Contains("plano Profissional", (await add.JsonAsync()).GetProperty("errors").GetProperty("deliveryMethod")[0].GetString());

        // Rascunho feito antes do downgrade: o envio recusa, sem gastar cota.
        var send = await SendAsync(admin, withSms);
        Assert.Equal(HttpStatusCode.Conflict, send.StatusCode);
        Assert.Equal("channel_not_in_plan", await CodeAsync(send));
        Assert.Equal(0, (await BillingAsync(admin)).GetProperty("contractsUsed").GetInt32());

        await SetPlanAsync(admin, "profissional");
        Assert.Equal(HttpStatusCode.Accepted, (await SendAsync(admin, withSms)).StatusCode);
    }

    [Fact]
    public async Task Users_beyond_the_plan_cannot_be_invited_or_reactivated()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        await SetPlanAsync(admin, "essencial"); // 3 usuários
        var editor = await TestAccounts.InviteAndAcceptAsync(factory, admin, "Editor");
        Assert.Equal(HttpStatusCode.Created, (await admin.Client.PostAsync("/api/v1/users/invite",
            new { email = TestAccounts.NewEmail(), role = "Leitor" })).StatusCode); // convite pendente conta

        var over = await admin.Client.PostAsync("/api/v1/users/invite", new { email = TestAccounts.NewEmail(), role = "Leitor" });
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Equal("user_limit", await CodeAsync(over));

        var users = await (await admin.Client.GetAsync("/api/v1/users")).JsonAsync();
        var editorId = users.EnumerateArray().Single(u => u.GetProperty("email").GetString() == editor.Email).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PutAsync($"/api/v1/users/{editorId}", new { active = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.Client.PostAsync("/api/v1/users/invite",
            new { email = TestAccounts.NewEmail(), role = "Leitor" })).StatusCode); // a vaga liberada foi usada

        var reactivate = await admin.Client.PutAsync($"/api/v1/users/{editorId}", new { active = true });
        Assert.Equal(HttpStatusCode.Conflict, reactivate.StatusCode);
        Assert.Equal("user_limit", await CodeAsync(reactivate));
    }

    [Fact]
    public async Task Templates_beyond_the_plan_cannot_be_created_duplicated_or_unarchived()
    {
        var (admin, first) = await AccountAsync("avulso"); // 2 templates
        var second = (await (await admin.Client.PostAsync("/api/v1/templates", new { name = "Segundo" })).JsonAsync()).GetProperty("id").GetGuid();

        var create = await admin.Client.PostAsync("/api/v1/templates", new { name = "Terceiro" });
        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
        Assert.Equal("template_limit", await CodeAsync(create));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.PostAsync($"/api/v1/templates/{first}/duplicate")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/v1/templates/{second}/archive")).StatusCode);
        var third = await admin.Client.PostAsync("/api/v1/templates", new { name = "Terceiro" });
        Assert.Equal(HttpStatusCode.Created, third.StatusCode); // arquivado não conta

        var unarchive = await admin.Client.PostAsync($"/api/v1/templates/{second}/unarchive");
        Assert.Equal(HttpStatusCode.Conflict, unarchive.StatusCode);
        Assert.Equal(2, (await BillingAsync(admin)).GetProperty("templatesUsed").GetInt32());
    }

    // ---------- API da plataforma ----------

    [Fact]
    public async Task Platform_api_requires_the_key()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Platform(key: null).GetAsync("/api/platform/organizations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Platform("errada").GetAsync("/api/platform/organizations")).StatusCode);

        var admin = await TestAccounts.CreateAdminAsync(factory);
        var slug = await SlugAsync(admin);
        // Cookie de cliente não abre a API da plataforma.
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.Client.GetAsync("/api/platform/organizations")).StatusCode);

        var list = await (await Platform().GetAsync($"/api/platform/organizations?q={slug}")).JsonAsync();
        Assert.Equal("empresa", Assert.Single(list.EnumerateArray()).GetProperty("plan").GetString());
    }

    [Fact]
    public async Task Granting_credits_with_the_same_reference_is_idempotent()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        await GrantAsync(admin, 10, "pagamento-123");
        var again = await GrantAsync(admin, 10, "pagamento-123");
        Assert.Equal(10, again.GetProperty("creditsAvailable").GetInt32());
        var grant = Assert.Single(again.GetProperty("credits").EnumerateArray());
        var expires = grant.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expires, DateTimeOffset.UtcNow.AddMonths(12).AddMinutes(-5), DateTimeOffset.UtcNow.AddMonths(12).AddMinutes(5));
    }

    [Fact]
    public async Task Platform_api_validates_plan_and_quantity()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var slug = await SlugAsync(admin);
        var http = Platform();

        Assert.Equal(HttpStatusCode.BadRequest, (await http.PutAsJsonAsync($"/api/platform/organizations/{slug}/plan", new { plan = "ouro" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PutAsJsonAsync($"/api/platform/organizations/{slug}/plan",
            new { plan = "essencial", periodStart = DateTimeOffset.UtcNow })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync($"/api/platform/organizations/{slug}/credits", new { quantity = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PutAsJsonAsync("/api/platform/organizations/nao-existe/plan", new { plan = "essencial" })).StatusCode);
    }
}

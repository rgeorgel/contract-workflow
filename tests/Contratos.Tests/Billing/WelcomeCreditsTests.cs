using System.Net;
using System.Text.Json;
using Contratos.Tests.Infrastructure;
using Contratos.Tests.Templates;

namespace Contratos.Tests.Billing;

/// <summary>Cadastro como em produção: Avulso, com 3 contratos de boas-vindas por 30 dias.</summary>
[Collection(AvulsoCollection.Name)]
public sealed class WelcomeCreditsTests(AvulsoApiFactory factory)
{
    private static readonly object ValidValues = new Dictionary<string, string>
    {
        ["contratante.nome"] = "Café Aurora Ltda",
        ["contratante.cpf"] = "529.982.247-25",
        ["valor"] = "1500,50",
    };

    private static async Task<JsonElement> BillingAsync(TestAccount admin) =>
        await (await admin.Client.GetAsync("/api/v1/organization/billing")).JsonAsync();

    private static async Task<HttpResponseMessage> SendNewAsync(TestAccount admin, Guid templateId, string channel = "Email")
    {
        var id = (await (await admin.Client.PostAsync("/api/v1/contracts", new { templateId, title = "Contrato de teste" })).JsonAsync()).GetProperty("id").GetGuid();
        await admin.Client.PutAsync($"/api/v1/contracts/{id}", new { title = "Contrato de teste", variableValues = ValidValues, signingMode = "Parallel" });
        object signer = channel == "Email"
            ? new { name = "Marina Costa", email = "marina@cliente.com.br", deliveryMethod = "Email" }
            : new { name = "Marina Costa", phone = "+55 11 98765-4321", deliveryMethod = channel };
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/v1/contracts/{id}/signers", signer)).StatusCode);
        return await admin.Client.PostAsync($"/api/v1/contracts/{id}/send");
    }

    [Fact]
    public async Task Confirming_the_signup_gives_three_contracts_for_thirty_days()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var billing = await BillingAsync(admin);

        Assert.Equal("avulso", billing.GetProperty("plan").GetProperty("id").GetString());
        Assert.Equal(3, billing.GetProperty("creditsAvailable").GetInt32());
        var grant = Assert.Single(billing.GetProperty("credits").EnumerateArray());
        Assert.Equal("welcome", grant.GetProperty("source").GetString());
        Assert.InRange(grant.GetProperty("expiresAt").GetDateTimeOffset(), DateTimeOffset.UtcNow.AddDays(30).AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30).AddMinutes(5));
        Assert.Equal(1, billing.GetProperty("usersLimit").GetInt32());
        Assert.Equal(2, billing.GetProperty("templatesLimit").GetInt32());
    }

    [Fact]
    public async Task Welcome_contracts_use_any_channel_and_then_sending_stops()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var templateId = (await TemplatesApiTests.CreatePublishedAsync(admin)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Accepted, (await SendNewAsync(admin, templateId, "WhatsApp")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await SendNewAsync(admin, templateId, "Sms")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await SendNewAsync(admin, templateId)).StatusCode);

        var fourth = await SendNewAsync(admin, templateId);
        Assert.Equal(HttpStatusCode.Conflict, fourth.StatusCode);
        Assert.Contains("não tem créditos de contrato", (await fourth.JsonAsync()).GetProperty("title").GetString());

        // Avisos: resta 1 e acabaram.
        await factory.ProcessBackgroundAsync();
        var subjects = factory.Emails.To(admin.Email).Select(m => m.Subject).ToList();
        Assert.Contains(subjects, s => s.StartsWith("Resta 1 contrato", StringComparison.Ordinal));
        Assert.Contains(subjects, s => s.StartsWith("Os créditos de contrato", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Avulso_allows_one_user_and_two_templates()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var invite = await admin.Client.PostAsync("/api/v1/users/invite", new { email = TestAccounts.NewEmail(), role = "Editor" });
        Assert.Equal(HttpStatusCode.Conflict, invite.StatusCode);
        Assert.Equal("user_limit", (await invite.JsonAsync()).GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.Created, (await admin.Client.PostAsync("/api/v1/templates", new { name = "Primeiro" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.Client.PostAsync("/api/v1/templates", new { name = "Segundo" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.PostAsync("/api/v1/templates", new { name = "Terceiro" })).StatusCode);
    }
}

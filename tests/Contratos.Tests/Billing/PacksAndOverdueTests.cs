using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contratos.Tests.Infrastructure;
using Contratos.Tests.Templates;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Contratos.Tests.Billing;

[Collection(ApiCollection.Name)]
public sealed class PacksAndOverdueTests(ApiFactory factory)
{
    private FakeStripe Stripe => factory.Stripe;

    private static async Task<JsonElement> BillingAsync(TestAccount admin) =>
        await (await admin.Client.GetAsync("/api/v1/organization/billing")).JsonAsync();

    private static string SessionIdOf(string url) => url[(url.LastIndexOf('/') + 1)..];

    private static async Task<string> BuyAsync(TestAccount admin, int quantity)
    {
        var res = await admin.Client.PostAsync("/api/v1/billing/packs/checkout", new { quantity });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return SessionIdOf((await res.JsonAsync()).GetProperty("url").GetString()!);
    }

    private static Task<HttpResponseMessage> ConfirmAsync(TestAccount admin, string sessionId) =>
        admin.Client.PostAsync("/api/v1/billing/packs/confirm", new { sessionId });

    private async Task PostEventAsync(string type, JsonObject obj)
    {
        var (body, signature) = FakeStripe.Webhook(type, obj);
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/stripe") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.TryAddWithoutValidation("Stripe-Signature", signature);
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(req)).StatusCode);
        await factory.ProcessBackgroundAsync();
    }

    // ---------- Pacotes ----------

    [Fact]
    public async Task Buying_a_pack_charges_the_catalog_price_by_card_and_credits_once()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var sessionId = await BuyAsync(admin, 10);

        var form = Stripe.Posts("/v1/checkout/sessions").Last().Form;
        Assert.Equal("payment", form["mode"]);
        Assert.Equal("card", form["allowed_payment_method_types[0]"]);
        Assert.False(form.ContainsKey("allowed_payment_method_types[1]"));
        Assert.Equal("2990", form["line_items[0][price_data][unit_amount]"]);
        Assert.Equal("brl", form["line_items[0][price_data][currency]"]);
        Assert.Equal("Pacote de 10 contratos", form["line_items[0][price_data][product_data][name]"]);
        Assert.Equal("credit_pack", form["metadata[kind]"]);
        Assert.Equal("10", form["metadata[pack_quantity]"]);
        Assert.StartsWith("https://app.test/settings.html?pack=success&session_id={CHECKOUT_SESSION_ID}", form["success_url"]);

        // Ainda não pago: nada entra.
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(admin, sessionId)).StatusCode);
        Assert.Equal(0, (await BillingAsync(admin)).GetProperty("creditsAvailable").GetInt32());

        Stripe.CompletePackCheckout(sessionId);
        var billing = await (await ConfirmAsync(admin, sessionId)).JsonAsync();
        Assert.Equal(10, billing.GetProperty("creditsAvailable").GetInt32());
        var grant = Assert.Single(billing.GetProperty("credits").EnumerateArray());
        Assert.Equal("stripe", grant.GetProperty("source").GetString());
        Assert.Equal("Pacote de 10 contratos", grant.GetProperty("note").GetString());
        Assert.InRange(grant.GetProperty("expiresAt").GetDateTimeOffset(), DateTimeOffset.UtcNow.AddMonths(12).AddMinutes(-5), DateTimeOffset.UtcNow.AddMonths(12).AddMinutes(5));

        // A volta repetida e o webhook da mesma sessão não creditam de novo.
        await ConfirmAsync(admin, sessionId);
        await PostEventAsync("checkout.session.completed", new JsonObject { ["id"] = sessionId, ["object"] = "checkout.session", ["mode"] = "payment" });
        Assert.Equal(10, (await BillingAsync(admin)).GetProperty("creditsAvailable").GetInt32());
    }

    [Fact]
    public async Task Webhook_alone_credits_the_pack_when_the_customer_never_comes_back()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var sessionId = await BuyAsync(admin, 5);
        Stripe.CompletePackCheckout(sessionId);

        await PostEventAsync("checkout.session.completed", new JsonObject { ["id"] = sessionId, ["object"] = "checkout.session", ["mode"] = "payment" });

        Assert.Equal(5, (await BillingAsync(admin)).GetProperty("creditsAvailable").GetInt32());
    }

    [Fact]
    public async Task A_paid_amount_different_from_the_catalog_credits_nothing()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var sessionId = await BuyAsync(admin, 50);
        Stripe.CompletePackCheckout(sessionId);
        Stripe.Sessions[sessionId]["amount_total"] = 390; // pagou o preço de 1, pediu 50

        await ConfirmAsync(admin, sessionId);
        Assert.Equal(0, (await BillingAsync(admin)).GetProperty("creditsAvailable").GetInt32());
    }

    [Fact]
    public async Task Packs_are_validated_and_belong_to_the_buyer()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PostAsync("/api/v1/billing/packs/checkout", new { quantity = 7 })).StatusCode);
        var editor = await TestAccounts.InviteAndAcceptAsync(factory, admin, "Editor");
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.Client.PostAsync("/api/v1/billing/packs/checkout", new { quantity = 5 })).StatusCode);

        var sessionId = await BuyAsync(admin, 5);
        Stripe.CompletePackCheckout(sessionId);
        var other = await TestAccounts.CreateAdminAsync(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await ConfirmAsync(other, sessionId)).StatusCode);
        Assert.Equal(0, (await BillingAsync(other)).GetProperty("creditsAvailable").GetInt32());
    }

    // ---------- Pagamento atrasado ----------

    [Fact]
    public async Task Overdue_payment_blocks_only_new_things_until_it_is_paid()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var editor = await TestAccounts.InviteAndAcceptAsync(factory, admin, "Editor");
        var templateId = (await TemplatesApiTests.CreatePublishedAsync(admin)).GetProperty("id").GetGuid();
        var sent = await DraftAsync(admin, templateId, "Contrato enviado");
        Assert.Equal(HttpStatusCode.Accepted, (await admin.Client.PostAsync($"/api/v1/contracts/{sent}/send")).StatusCode);
        var draft = await DraftAsync(admin, templateId, "Rascunho pendente");

        var slug = (await (await admin.Client.GetAsync("/api/v1/organization/settings")).JsonAsync()).GetProperty("slug").GetString();
        using (var platform = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }))
        {
            platform.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.PlatformKey);
            await platform.PutAsJsonAsync($"/api/platform/organizations/{slug}/plan", new { plan = "avulso" });
        }
        var checkout = await admin.Client.PostAsync("/api/v1/billing/checkout", new { plan = "profissional", interval = "month" });
        var sessionId = SessionIdOf((await checkout.JsonAsync()).GetProperty("url").GetString()!);
        var subscriptionId = Stripe.CompleteCheckout(sessionId);
        await admin.Client.PostAsync("/api/v1/billing/checkout/confirm", new { sessionId });

        // A renovação foi recusada no cartão.
        Stripe.UpdateSubscription(subscriptionId, status: "past_due");
        await PostEventAsync("invoice.payment_failed", new JsonObject
        {
            ["id"] = "in_1", ["object"] = "invoice",
            ["parent"] = new JsonObject { ["subscription_details"] = new JsonObject { ["subscription"] = subscriptionId } },
        });

        // Nada novo: contrato, envio, template e usuário.
        async Task AssertBlockedAsync(HttpResponseMessage res)
        {
            Assert.Equal(HttpStatusCode.PaymentRequired, res.StatusCode);
            Assert.Equal("payment_overdue", (await res.JsonAsync()).GetProperty("code").GetString());
        }
        await AssertBlockedAsync(await editor.Client.PostAsync("/api/v1/contracts", new { templateId, title = "Novo contrato" }));
        await AssertBlockedAsync(await editor.Client.PostAsync($"/api/v1/contracts/{draft}/send"));
        await AssertBlockedAsync(await admin.Client.PostAsync("/api/v1/templates", new { name = "Novo template" }));
        await AssertBlockedAsync(await admin.Client.PostAsync($"/api/v1/templates/{templateId}/duplicate"));
        await AssertBlockedAsync(await admin.Client.PostAsync("/api/v1/users/invite", new { email = TestAccounts.NewEmail(), role = "Leitor" }));

        // O que já existe continua acessível: listar, ver, baixar e cancelar os contratos já pagos.
        Assert.Equal(HttpStatusCode.OK, (await editor.Client.GetAsync("/api/v1/contracts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.Client.GetAsync($"/api/v1/contracts/{sent}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.Client.GetAsync($"/api/v1/contracts/{sent}/document")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.Client.GetAsync("/api/v1/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync("/api/v1/dashboard/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.Client.PutAsync($"/api/v1/contracts/{draft}",
            new { title = "Rascunho editado", variableValues = ValidValues, signingMode = "Parallel" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/v1/contracts/{sent}/cancel")).StatusCode);

        // As telas sabem do bloqueio (aviso no topo e em Configurações).
        var me = await (await editor.Client.GetAsync("/api/v1/auth/me")).JsonAsync();
        Assert.True(me.GetProperty("organization").GetProperty("paymentOverdue").GetBoolean());
        Assert.True((await BillingAsync(admin)).GetProperty("paymentOverdue").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync("/api/v1/billing/portal")).StatusCode);

        // Os Admins recebem um aviso, uma vez por atraso.
        await PostEventAsync("customer.subscription.updated", new JsonObject { ["id"] = subscriptionId, ["object"] = "subscription" });
        var alert = Assert.Single(factory.Emails.To(admin.Email), m => m.Subject.StartsWith("Pagamento recusado", StringComparison.Ordinal));
        Assert.Contains("Os contratos existentes continuam disponíveis", alert.TextBody);
        Assert.DoesNotContain(factory.Emails.To(editor.Email), m => m.Subject.StartsWith("Pagamento recusado", StringComparison.Ordinal));

        // Pagou: libera na hora (Configurações confere o Stripe ao abrir, mesmo antes do webhook).
        Stripe.UpdateSubscription(subscriptionId, status: "active");
        Assert.False((await BillingAsync(admin)).GetProperty("paymentOverdue").GetBoolean());
        Assert.Equal(HttpStatusCode.Accepted, (await editor.Client.PostAsync($"/api/v1/contracts/{draft}/send")).StatusCode);
    }

    private static readonly object ValidValues = new Dictionary<string, string>
    {
        ["contratante.nome"] = "Café Aurora Ltda",
        ["contratante.cpf"] = "529.982.247-25",
        ["valor"] = "1500,50",
    };

    private static async Task<Guid> DraftAsync(TestAccount admin, Guid templateId, string title)
    {
        var created = await admin.Client.PostAsync("/api/v1/contracts", new { templateId, title });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.JsonAsync()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PutAsync($"/api/v1/contracts/{id}",
            new { title, variableValues = ValidValues, signingMode = "Parallel" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/v1/contracts/{id}/signers",
            new { name = "Marina Costa", email = "marina@cliente.com.br", deliveryMethod = "Email" })).StatusCode);
        return id;
    }
}

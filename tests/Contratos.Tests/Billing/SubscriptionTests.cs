using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contratos.Infrastructure.Billing.Stripe;
using Contratos.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contratos.Tests.Billing;

[Collection(ApiCollection.Name)]
public sealed class SubscriptionTests(ApiFactory factory)
{
    private FakeStripe Stripe => factory.Stripe;

    // ---------- Apoio ----------

    private static async Task<JsonElement> BillingAsync(TestAccount admin) =>
        await (await admin.Client.GetAsync("/api/v1/organization/billing")).JsonAsync();

    private static async Task<Guid> OrgIdAsync(TestAccount admin) =>
        (await (await admin.Client.GetAsync("/api/v1/organization/settings")).JsonAsync()).GetProperty("id").GetGuid();

    /// <summary>Conta no plano grátis (o padrão dos testes é Empresa, sem assinatura).</summary>
    private async Task<TestAccount> FreeAccountAsync()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var slug = (await (await admin.Client.GetAsync("/api/v1/organization/settings")).JsonAsync()).GetProperty("slug").GetString();
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.PlatformKey);
        Assert.Equal(HttpStatusCode.OK, (await http.PutAsJsonAsync($"/api/platform/organizations/{slug}/plan", new { plan = "gratis" })).StatusCode);
        return admin;
    }

    private static async Task<string> CheckoutAsync(TestAccount admin, string plan = "essencial", string interval = "month")
    {
        var res = await admin.Client.PostAsync("/api/v1/billing/checkout", new { plan, interval });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var url = (await res.JsonAsync()).GetProperty("url").GetString()!;
        return url[(url.LastIndexOf('/') + 1)..]; // cs_test_...
    }

    /// <summary>Assina e volta do pagamento: devolve a assinatura criada no Stripe.</summary>
    private async Task<string> SubscribeAsync(TestAccount admin, string plan = "essencial", string interval = "month")
    {
        var sessionId = await CheckoutAsync(admin, plan, interval);
        var subscriptionId = Stripe.CompleteCheckout(sessionId);
        var confirm = await admin.Client.PostAsync("/api/v1/billing/checkout/confirm", new { sessionId });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        return subscriptionId;
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string body, string signature)
    {
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/stripe") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.TryAddWithoutValidation("Stripe-Signature", signature);
        return await http.SendAsync(req);
    }

    private async Task SubscriptionEventAsync(string type, string subscriptionId, string? eventId = null)
    {
        var (body, signature) = FakeStripe.Webhook(type, new JsonObject { ["id"] = subscriptionId, ["object"] = "subscription", ["status"] = "ignorado" }, eventId);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(body, signature)).StatusCode);
        await factory.ProcessBackgroundAsync();
    }

    // ---------- Checkout ----------

    [Fact]
    public async Task Checkout_creates_the_catalog_in_stripe_and_accepts_only_cards()
    {
        var admin = await FreeAccountAsync();
        var sessionId = await CheckoutAsync(admin, "essencial", "month");

        // Catálogo: preço mensal e anual de cada plano pago, em centavos de real, achados pela lookup key.
        Assert.Equal(3990, Stripe.Prices[Stripe.PriceId("contratos_essencial_mensal")]["unit_amount"]!.GetValue<long>());
        Assert.Equal(39900, Stripe.Prices[Stripe.PriceId("contratos_essencial_anual")]["unit_amount"]!.GetValue<long>());
        Assert.Equal("year", Stripe.Prices[Stripe.PriceId("contratos_empresa_anual")]["recurring"]!["interval"]!.GetValue<string>());
        Assert.DoesNotContain(Stripe.Prices.Values, p => p["lookup_key"]?.GetValue<string>()?.Contains("gratis") == true);

        var form = Stripe.Posts("/v1/checkout/sessions").Last().Form;
        Assert.Equal("subscription", form["mode"]);
        Assert.Equal("card", form["allowed_payment_method_types[0]"]);
        Assert.False(form.ContainsKey("allowed_payment_method_types[1]"));
        Assert.Equal(Stripe.PriceId("contratos_essencial_mensal"), form["line_items[0][price]"]);
        Assert.Equal((await OrgIdAsync(admin)).ToString(), form["client_reference_id"]);
        Assert.Equal((await OrgIdAsync(admin)).ToString(), form["subscription_data[metadata][organization_id]"]);
        Assert.Equal("https://app.test/settings.html?checkout=success&session_id={CHECKOUT_SESSION_ID}", form["success_url"]);
        Assert.Equal("pt-BR", form["locale"]);
        Assert.StartsWith("cs_test_", sessionId);

        // Nada muda antes de pagar.
        Assert.Equal("gratis", (await BillingAsync(admin)).GetProperty("plan").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Returning_from_payment_activates_the_plan_with_the_stripe_cycle()
    {
        var admin = await FreeAccountAsync();
        await SubscribeAsync(admin, "profissional", "year");

        var billing = await BillingAsync(admin);
        Assert.Equal("profissional", billing.GetProperty("plan").GetProperty("id").GetString());
        Assert.Equal(80, billing.GetProperty("contractsLimit").GetInt32());
        Assert.Equal(FakeStripe.PeriodStart, billing.GetProperty("periodStart").GetDateTimeOffset());
        Assert.Equal(FakeStripe.PeriodEnd("year"), billing.GetProperty("periodEnd").GetDateTimeOffset());
        var sub = billing.GetProperty("subscription");
        Assert.Equal("active", sub.GetProperty("status").GetString());
        Assert.Equal("year", sub.GetProperty("interval").GetString());
        Assert.False(sub.GetProperty("cancelAtPeriodEnd").GetBoolean());

        // Já assina: não abre outro checkout; e a plataforma não troca o plano por fora do Stripe.
        var again = await admin.Client.PostAsync("/api/v1/billing/checkout", new { plan = "empresa", interval = "month" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already_subscribed", (await again.JsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_checkout_session_of_another_organization_does_not_activate_anything()
    {
        var payer = await FreeAccountAsync();
        var sessionId = await CheckoutAsync(payer);
        Stripe.CompleteCheckout(sessionId);

        var other = await FreeAccountAsync();
        var res = await other.Client.PostAsync("/api/v1/billing/checkout/confirm", new { sessionId });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("gratis", (await BillingAsync(other)).GetProperty("plan").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Only_admins_subscribe_and_plans_must_be_paid()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory); // Empresa sem assinatura: cabe o convite
        var editor = await TestAccounts.InviteAndAcceptAsync(factory, admin, "Editor");
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.Client.PostAsync("/api/v1/billing/checkout", new { plan = "essencial", interval = "month" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PostAsync("/api/v1/billing/checkout", new { plan = "gratis", interval = "month" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PostAsync("/api/v1/billing/checkout", new { plan = "essencial", interval = "week" })).StatusCode);
        Assert.True((await BillingAsync(admin)).GetProperty("onlineCheckout").GetBoolean());
    }

    // ---------- Webhooks ----------

    [Fact]
    public async Task Webhook_with_a_bad_signature_is_refused()
    {
        var (body, _) = FakeStripe.Webhook("customer.subscription.updated", new JsonObject { ["id"] = "sub_x" });
        Assert.Equal(HttpStatusCode.BadRequest, (await PostWebhookAsync(body, "t=1,v1=abc")).StatusCode);
        var (_, wrongSecret) = FakeStripe.Webhook("customer.subscription.updated", new JsonObject { ["id"] = "sub_x" }, secret: "whsec_outro");
        Assert.Equal(HttpStatusCode.BadRequest, (await PostWebhookAsync(body, wrongSecret)).StatusCode);
    }

    [Fact]
    public async Task Plan_changes_and_cancellation_arrive_by_webhook()
    {
        var admin = await FreeAccountAsync();
        var subscriptionId = await SubscribeAsync(admin, "essencial", "month");

        // Upgrade no portal: o webhook traz só o id; o sistema busca o estado atual.
        Stripe.UpdateSubscription(subscriptionId, priceId: Stripe.PriceId("contratos_empresa_mensal"));
        await SubscriptionEventAsync("customer.subscription.updated", subscriptionId);
        Assert.Equal("empresa", (await BillingAsync(admin)).GetProperty("plan").GetProperty("id").GetString());

        // Cancelou no portal: continua no plano até o fim do ciclo.
        Stripe.UpdateSubscription(subscriptionId, cancelAtPeriodEnd: true);
        await SubscriptionEventAsync("customer.subscription.updated", subscriptionId);
        var billing = await BillingAsync(admin);
        Assert.Equal("empresa", billing.GetProperty("plan").GetProperty("id").GetString());
        Assert.True(billing.GetProperty("subscription").GetProperty("cancelAtPeriodEnd").GetBoolean());

        // Fim do ciclo: o Stripe encerra e a conta volta ao grátis.
        Stripe.UpdateSubscription(subscriptionId, status: "canceled");
        await SubscriptionEventAsync("customer.subscription.deleted", subscriptionId);
        billing = await BillingAsync(admin);
        Assert.Equal("gratis", billing.GetProperty("plan").GetProperty("id").GetString());
        Assert.Equal("canceled", billing.GetProperty("subscription").GetProperty("status").GetString());

        // Pode assinar de novo.
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync("/api/v1/billing/checkout", new { plan = "essencial", interval = "month" })).StatusCode);
    }

    [Fact]
    public async Task Late_payment_keeps_the_plan_and_unpaid_ends_it()
    {
        var admin = await FreeAccountAsync();
        var subscriptionId = await SubscribeAsync(admin, "essencial", "month");

        Stripe.UpdateSubscription(subscriptionId, status: "past_due");
        var invoice = new JsonObject
        {
            ["id"] = "in_1", ["object"] = "invoice",
            ["parent"] = new JsonObject { ["subscription_details"] = new JsonObject { ["subscription"] = subscriptionId } },
        };
        var (body, signature) = FakeStripe.Webhook("invoice.payment_failed", invoice);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(body, signature)).StatusCode);
        await factory.ProcessBackgroundAsync();
        var billing = await BillingAsync(admin);
        Assert.Equal("essencial", billing.GetProperty("plan").GetProperty("id").GetString());
        Assert.Equal("past_due", billing.GetProperty("subscription").GetProperty("status").GetString());

        Stripe.UpdateSubscription(subscriptionId, status: "unpaid");
        await SubscriptionEventAsync("customer.subscription.updated", subscriptionId);
        Assert.Equal("gratis", (await BillingAsync(admin)).GetProperty("plan").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Checkout_completed_webhook_activates_even_if_the_customer_never_comes_back()
    {
        var admin = await FreeAccountAsync();
        var sessionId = await CheckoutAsync(admin, "essencial", "month");
        var subscriptionId = Stripe.CompleteCheckout(sessionId);

        var session = new JsonObject { ["id"] = sessionId, ["object"] = "checkout.session", ["mode"] = "subscription", ["subscription"] = subscriptionId };
        var eventId = $"evt_{Guid.NewGuid():N}";
        var (body, signature) = FakeStripe.Webhook("checkout.session.completed", session, eventId);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(body, signature)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(body, signature)).StatusCode); // reentrega: um só registro
        await factory.ProcessBackgroundAsync();

        Assert.Equal("essencial", (await BillingAsync(admin)).GetProperty("plan").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Platform_cannot_override_a_plan_managed_by_stripe()
    {
        var admin = await FreeAccountAsync();
        await SubscribeAsync(admin);
        var slug = (await (await admin.Client.GetAsync("/api/v1/organization/settings")).JsonAsync()).GetProperty("slug").GetString();
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.PlatformKey);
        var res = await http.PutAsJsonAsync($"/api/platform/organizations/{slug}/plan", new { plan = "empresa" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("managed_by_stripe", (await res.JsonAsync()).GetProperty("code").GetString());
    }

    // ---------- Portal ----------

    [Fact]
    public async Task Portal_opens_for_customers_with_plan_switching_configured()
    {
        var newcomer = await FreeAccountAsync();
        var none = await newcomer.Client.PostAsync("/api/v1/billing/portal");
        Assert.Equal(HttpStatusCode.Conflict, none.StatusCode);
        Assert.Equal("no_subscription", (await none.JsonAsync()).GetProperty("code").GetString());

        var admin = await FreeAccountAsync();
        await SubscribeAsync(admin);
        var res = await admin.Client.PostAsync("/api/v1/billing/portal");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("https://billing.stripe.test/", (await res.JsonAsync()).GetProperty("url").GetString());

        var portal = Stripe.Posts("/v1/billing_portal/sessions").Last().Form;
        Assert.Equal("https://app.test/settings.html", portal["return_url"]);
        var config = Stripe.PortalConfigs[portal["configuration"]]["features_form"]!.AsObject();
        Assert.Equal("always_invoice", config["features[subscription_update][proration_behavior]"]!.GetValue<string>());
        Assert.Equal("at_period_end", config["features[subscription_cancel][mode]"]!.GetValue<string>());
        Assert.Equal("contratos_profissional", config["features[subscription_update][products][1][product]"]!.GetValue<string>());
    }

    // ---------- Catálogo ----------

    [Fact]
    public async Task Price_changed_in_code_creates_a_new_stripe_price_and_moves_the_lookup_key()
    {
        var fake = new FakeStripe();
        var oldId = "price_antigo";
        fake.Prices[oldId] = new JsonObject
        {
            ["id"] = oldId, ["object"] = "price", ["active"] = true, ["currency"] = "brl", ["unit_amount"] = 2990,
            ["lookup_key"] = "contratos_essencial_mensal", ["product"] = "contratos_essencial", ["type"] = "recurring",
            ["recurring"] = new JsonObject { ["interval"] = "month", ["interval_count"] = 1 }, ["metadata"] = new JsonObject(),
        };
        var client = new global::Stripe.StripeClient(FakeStripe.SecretKey,
            httpClient: new global::Stripe.SystemNetHttpClient(new HttpClient(fake)), apiBase: "https://api.stripe.test");
        var catalog = new StripeCatalog(client, NullLogger<StripeCatalog>.Instance);

        var synced = await catalog.EnsureAsync(CancellationToken.None);

        var current = synced.Prices[("essencial", "month")];
        Assert.NotEqual(oldId, current);
        Assert.Equal(3990, fake.Prices[current]["unit_amount"]!.GetValue<long>());
        Assert.Null(fake.Prices[oldId]["lookup_key"]); // a chave passou para o preço novo
        Assert.Equal(6, synced.Prices.Count);
        Assert.Same(synced, await catalog.EnsureAsync(CancellationToken.None)); // sincroniza uma vez por processo
    }

    [Theory]
    [InlineData("customer.subscription.updated", """{"id":"sub_1"}""", "sub_1")]
    [InlineData("checkout.session.completed", """{"id":"cs_1","mode":"subscription","subscription":"sub_2"}""", "sub_2")]
    [InlineData("checkout.session.completed", """{"id":"cs_1","mode":"payment","subscription":null}""", null)]
    [InlineData("invoice.paid", """{"id":"in_1","parent":{"subscription_details":{"subscription":"sub_3"}}}""", "sub_3")]
    [InlineData("invoice.paid", """{"id":"in_1","subscription":"sub_4"}""", "sub_4")]
    public void Subscription_id_is_found_in_each_event_shape(string type, string json, string? expected)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, StripeSubscriptionSync.SubscriptionIdOf(type, doc.RootElement));
    }
}

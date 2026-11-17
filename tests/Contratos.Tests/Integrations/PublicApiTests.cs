using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Contratos.Application.Integrations;
using Contratos.Infrastructure.Integrations;
using Contratos.Tests.Infrastructure;
using Contratos.Tests.Templates;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Contratos.Tests.Integrations;

[Collection(ApiCollection.Name)]
public sealed class PublicApiTests(ApiFactory factory)
{
    private static readonly object Variables = new Dictionary<string, string>
    {
        ["contratante.nome"] = "Café Aurora Ltda",
        ["contratante.cpf"] = "529.982.247-25",
        ["valor"] = "1500,50",
    };

    // ---------- Apoio ----------

    private HttpClient Public(string? token)
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (token is not null)
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<string> CreateKeyAsync(TestAccount admin, string name = "CRM")
    {
        var res = await admin.Client.PostAsync("/api/v1/integrations/keys", new { name });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.JsonAsync()).GetProperty("token").GetString()!;
    }

    private static async Task<(TestAccount Admin, HttpClient Api, Guid TemplateId)> SetupAsync(ApiFactory factory, PublicApiTests t)
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var templateId = (await TemplatesApiTests.CreatePublishedAsync(admin)).GetProperty("id").GetGuid();
        return (admin, t.Public(await CreateKeyAsync(admin)), templateId);
    }

    private static object NewContract(Guid templateId, bool send = false, string? externalReference = null) => new
    {
        templateId,
        title = "Consultoria Café Aurora",
        variables = Variables,
        signers = new object[] { new { name = "Marina Costa", email = "marina@cliente.com.br", deliveryMethod = "email" } },
        externalReference,
        send,
    };

    private static async Task<JsonElement> CreateWebhookAsync(TestAccount admin, string url, params string[] events)
    {
        var res = await admin.Client.PostAsync("/api/v1/integrations/webhooks", new { url, events });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.JsonAsync();
    }

    private async Task SimulateAsync(TestAccount admin, Guid contractId, Guid signerId, string action) =>
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.Client.PostAsync($"/api/v1/dev/fake-provider/contracts/{contractId}/signers/{signerId}/{action}")).StatusCode);

    private static string Url() => $"https://crm.test/hooks/{Guid.NewGuid():N}";

    // ---------- Chaves ----------

    [Fact]
    public async Task Key_is_shown_once_and_only_authenticates_the_public_api()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var created = await (await admin.Client.PostAsync("/api/v1/integrations/keys", new { name = "ERP" })).JsonAsync();
        var token = created.GetProperty("token").GetString()!;
        Assert.StartsWith("ctr_", token);
        Assert.True(token.Length > 40);

        var overview = await (await admin.Client.GetAsync("/api/v1/integrations")).JsonAsync();
        var key = Assert.Single(overview.GetProperty("keys").EnumerateArray());
        Assert.Equal("ERP", key.GetProperty("name").GetString());
        Assert.StartsWith(key.GetProperty("prefix").GetString()!, token);
        Assert.DoesNotContain(token, overview.GetRawText());

        var account = await Public(token).GetAsync("/api/public/v1/account");
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
        var body = await account.JsonAsync();
        Assert.Equal("empresa", body.GetProperty("organization").GetProperty("planId").GetString());
        Assert.Equal(250, body.GetProperty("usage").GetProperty("contractsLimit").GetInt32());

        // Sem chave, chave errada, ou o cookie da tela: 401 em JSON.
        var anonymous = await Public(null).GetAsync("/api/public/v1/account");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("unauthorized", (await anonymous.JsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Public(token + "x").GetAsync("/api/public/v1/account")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.Client.GetAsync("/api/public/v1/account")).StatusCode);
        // A chave não abre a API da tela.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Public(token).GetAsync("/api/v1/contracts")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.Client.DeleteAsync($"/api/v1/integrations/keys/{key.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Public(token).GetAsync("/api/public/v1/account")).StatusCode);
    }

    [Fact]
    public async Task Only_admins_manage_integrations()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var editor = await TestAccounts.InviteAndAcceptAsync(factory, admin, "Editor");
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.Client.GetAsync("/api/v1/integrations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.Client.PostAsync("/api/v1/integrations/keys", new { name = "x" })).StatusCode);
    }

    [Fact]
    public async Task Every_plan_includes_the_api_and_webhooks()
    {
        Assert.All(Contratos.Domain.Billing.Plans.All, p => Assert.True(p.ApiAccess, p.Id));

        var admin = await TestAccounts.CreateAdminAsync(factory);
        var token = await CreateKeyAsync(admin);
        var slug = (await (await admin.Client.GetAsync("/api/v1/organization/settings")).JsonAsync()).GetProperty("slug").GetString();
        var platform = Public(ApiFactory.PlatformKey);

        foreach (var plan in new[] { "avulso", "essencial", "profissional" })
        {
            Assert.Equal(HttpStatusCode.OK, (await platform.PutAsJsonAsync($"/api/platform/organizations/{slug}/plan", new { plan })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Public(token).GetAsync("/api/public/v1/account")).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await admin.Client.PostAsync("/api/v1/integrations/keys", new { name = $"chave {plan}" })).StatusCode);
            var overview = await (await admin.Client.GetAsync("/api/v1/integrations")).JsonAsync();
            Assert.True(overview.GetProperty("available").GetBoolean(), plan);
        }
    }

    // ---------- Templates e contratos ----------

    [Fact]
    public async Task Templates_list_the_published_ones_with_their_variables()
    {
        var (_, api, templateId) = await SetupAsync(factory, this);
        var list = await (await api.GetAsync("/api/public/v1/templates")).JsonAsync();
        var item = Assert.Single(list.EnumerateArray());
        Assert.Equal(templateId, item.GetProperty("id").GetGuid());
        Assert.Equal(3, item.GetProperty("variableCount").GetInt32());

        var detail = await (await api.GetAsync($"/api/public/v1/templates/{templateId}")).JsonAsync();
        var cpf = detail.GetProperty("variables").EnumerateArray().Single(v => v.GetProperty("key").GetString() == "contratante.cpf");
        Assert.Equal("cpf_cnpj", cpf.GetProperty("type").GetString());
        Assert.Equal("sign_as_witness", detail.GetProperty("suggestedSigners")[1].GetProperty("action").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/api/public/v1/templates/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Creating_and_sending_in_one_call_is_idempotent()
    {
        var (admin, api, templateId) = await SetupAsync(factory, this);
        var reference = $"crm-{Guid.NewGuid():N}";
        using var first = new HttpRequestMessage(HttpMethod.Post, "/api/public/v1/contracts")
        {
            Content = JsonContent.Create(NewContract(templateId, send: true, externalReference: reference)),
        };
        first.Headers.Add("Idempotency-Key", reference);
        var created = await api.SendAsync(first);
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var contract = await created.JsonAsync();
        var id = contract.GetProperty("id").GetGuid();
        Assert.Equal($"/api/public/v1/contracts/{id}", created.Headers.Location?.ToString());
        Assert.Equal("sent", contract.GetProperty("status").GetString());
        Assert.Equal(reference, contract.GetProperty("externalReference").GetString());
        Assert.Equal("email", contract.GetProperty("signers")[0].GetProperty("deliveryMethod").GetString());
        Assert.Equal("1500,50", contract.GetProperty("variables").GetProperty("valor").GetString());

        // A rede caiu e o CRM repetiu: devolve o mesmo contrato, sem criar nem enviar outro.
        using var again = new HttpRequestMessage(HttpMethod.Post, "/api/public/v1/contracts")
        {
            Content = JsonContent.Create(NewContract(templateId, send: true, externalReference: reference)),
        };
        again.Headers.Add("Idempotency-Key", reference);
        var replay = await api.SendAsync(again);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(id, (await replay.JsonAsync()).GetProperty("id").GetGuid());

        var list = await (await api.GetAsync($"/api/public/v1/contracts?externalReference={reference}")).JsonAsync();
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.False(list.GetProperty("hasMore").GetBoolean());
        Assert.Equal(id, list.GetProperty("items")[0].GetProperty("id").GetGuid());

        // Criado pela chave: a tela mostra o contrato normalmente, e a linha do tempo diz qual chave.
        var events = await (await admin.Client.GetAsync($"/api/v1/contracts/{id}/events")).JsonAsync();
        var createdEvent = events.EnumerateArray().Single(e => e.GetProperty("type").GetString() == "Created");
        Assert.Equal("CRM", createdEvent.GetProperty("payload").GetProperty("apiKey").GetString());
        var publicEvents = await (await api.GetAsync($"/api/public/v1/contracts/{id}/events")).JsonAsync();
        Assert.Contains(publicEvents.EnumerateArray(), e => e.GetProperty("type").GetString() == "contract_sent");
    }

    [Fact]
    public async Task Validation_errors_name_the_fields_of_the_public_body()
    {
        var (_, api, templateId) = await SetupAsync(factory, this);
        var res = await api.PostAsJsonAsync("/api/public/v1/contracts", new
        {
            templateId,
            variables = new Dictionary<string, string> { ["contratante.nome"] = "Café Aurora", ["valor"] = "abc" },
            signers = new object[] { new { name = "Marina", email = "isto-nao-e-email", deliveryMethod = "email" } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await res.JsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("variables.valor", out _));
        Assert.True(errors.TryGetProperty("variables.contratante.cpf", out _));
        Assert.True(errors.TryGetProperty("signers[0].email", out _));

        var badEnum = await api.PostAsync("/api/public/v1/contracts",
            new StringContent("""{"templateId":"%ID%","signers":[{"name":"A","deliveryMethod":"pombo"}]}""".Replace("%ID%", templateId.ToString()), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, badEnum.StatusCode);
        Assert.Equal("invalid_json", (await badEnum.JsonAsync()).GetProperty("code").GetString());

        var badStatus = await api.GetAsync("/api/public/v1/contracts?status=assinado");
        Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/public/v1/contracts?status=partially_signed")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("/api/public/v1/nao-existe")).StatusCode);
    }

    [Fact]
    public async Task Oversized_bodies_are_refused()
    {
        var (_, api, templateId) = await SetupAsync(factory, this);
        var res = await api.PostAsJsonAsync("/api/public/v1/contracts", new { templateId, title = new string('a', 300 * 1024) });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
        Assert.Equal("payload_too_large", (await res.JsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_draft_created_by_the_api_can_be_sent_and_cancelled_later()
    {
        var (_, api, templateId) = await SetupAsync(factory, this);
        var draft = await (await api.PostAsJsonAsync("/api/public/v1/contracts", NewContract(templateId))).JsonAsync();
        Assert.Equal("draft", draft.GetProperty("status").GetString());
        var id = draft.GetProperty("id").GetGuid();

        var sent = await api.PostAsync($"/api/public/v1/contracts/{id}/send", null);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        var again = await api.PostAsync($"/api/public/v1/contracts/{id}/send", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already_sent", (await again.JsonAsync()).GetProperty("code").GetString());

        var cancelled = await (await api.PostAsync($"/api/public/v1/contracts/{id}/cancel", null)).JsonAsync();
        Assert.Equal("cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/api/public/v1/contracts/{id}/signed-document")).StatusCode);
    }

    [Fact]
    public async Task Sending_a_cancelled_draft_says_it_is_final_not_sent()
    {
        var (_, api, templateId) = await SetupAsync(factory, this);
        var id = (await (await api.PostAsJsonAsync("/api/public/v1/contracts", NewContract(templateId))).JsonAsync()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await api.PostAsync($"/api/public/v1/contracts/{id}/cancel", null)).StatusCode);

        var send = await api.PostAsync($"/api/public/v1/contracts/{id}/send", null);
        Assert.Equal(HttpStatusCode.Conflict, send.StatusCode);
        var error = await send.JsonAsync();
        Assert.Equal("already_final", error.GetProperty("code").GetString());
        Assert.Equal("Este contrato já foi finalizado.", error.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("whatsapp")]
    [InlineData("whats_app")] // valor que as respostas usavam até 16/11/2026: continua aceito na entrada
    [InlineData("WhatsApp")]
    public async Task Whatsapp_channel_always_comes_back_as_documented(string sent)
    {
        var (_, api, templateId) = await SetupAsync(factory, this);
        var created = await api.PostAsJsonAsync("/api/public/v1/contracts", new
        {
            templateId,
            title = "Consultoria Café Aurora",
            variables = Variables,
            signers = new object[] { new { name = "Testemunha", phone = "+55 11 90000-0000", deliveryMethod = sent } },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains("\"deliveryMethod\":\"whatsapp\"", await created.Content.ReadAsStringAsync());

        var id = (await created.JsonAsync()).GetProperty("id").GetGuid();
        var detail = await (await api.GetAsync($"/api/public/v1/contracts/{id}")).JsonAsync();
        Assert.Equal("whatsapp", detail.GetProperty("signers")[0].GetProperty("deliveryMethod").GetString());
        // Contrato ainda não enviado: sem link de assinatura.
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("signers")[0].GetProperty("signLink").ValueKind);
    }

    [Fact]
    public async Task Unknown_channel_is_rejected()
    {
        var (_, api, templateId) = await SetupAsync(factory, this);
        var res = await api.PostAsJsonAsync("/api/public/v1/contracts", new
        {
            templateId,
            signers = new object[] { new { name = "Testemunha", phone = "+55 11 90000-0000", deliveryMethod = "telegram" } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task One_organization_never_sees_another_one_through_the_api()
    {
        var (_, apiA, templateA) = await SetupAsync(factory, this);
        var (_, apiB, _) = await SetupAsync(factory, this);
        var id = (await (await apiA.PostAsJsonAsync("/api/public/v1/contracts", NewContract(templateA))).JsonAsync()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await apiB.GetAsync($"/api/public/v1/contracts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await apiB.GetAsync($"/api/public/v1/templates/{templateA}")).StatusCode);
        var created = await apiB.PostAsJsonAsync("/api/public/v1/contracts", NewContract(templateA));
        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
    }

    // ---------- Webhooks ----------

    [Fact]
    public async Task Signature_events_reach_the_customer_signed_with_the_endpoint_secret()
    {
        var (admin, api, templateId) = await SetupAsync(factory, this);
        var url = Url();
        var endpoint = await CreateWebhookAsync(admin, url, "signer.signed", "contract.completed", "contract.signed_document_ready");
        var secret = endpoint.GetProperty("secret").GetString()!;
        Assert.StartsWith("whsec_", secret);

        var contract = await (await api.PostAsJsonAsync("/api/public/v1/contracts", NewContract(templateId, send: true, externalReference: "pedido-42"))).JsonAsync();
        var id = contract.GetProperty("id").GetGuid();
        var signerId = contract.GetProperty("signers")[0].GetProperty("id").GetGuid();
        await factory.ProcessBackgroundAsync(); // entrega ao provedor
        Assert.Empty(factory.Webhooks.To(url)); // contract.sent não foi assinado

        await SimulateAsync(admin, id, signerId, "sign");
        await factory.ProcessBackgroundAsync(); // guarda o PDF assinado
        var received = factory.Webhooks.To(url);
        Assert.Equal(["signer.signed", "contract.completed", "contract.signed_document_ready"], received.Select(r => r.Header("X-Contratos-Event")));

        foreach (var r in received)
        {
            var header = r.Header(WebhookSignature.Header);
            var t = long.Parse(header.Split(',')[0][2..], System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(WebhookSignature.Compute(secret, t, r.Body), header);
            Assert.InRange(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - t, 0, 60);
        }
        var signed = JsonDocument.Parse(received[0].Body).RootElement;
        Assert.Equal("signer.signed", signed.GetProperty("type").GetString());
        Assert.Equal(received[0].Header("X-Contratos-Event-Id"), signed.GetProperty("id").GetString());
        Assert.Equal("pedido-42", signed.GetProperty("data").GetProperty("contract").GetProperty("externalReference").GetString());
        Assert.Equal("Marina Costa", signed.GetProperty("data").GetProperty("signer").GetProperty("name").GetString());
        Assert.Equal("signed", signed.GetProperty("data").GetProperty("signer").GetProperty("status").GetString());
        var ready = JsonDocument.Parse(received[2].Body).RootElement.GetProperty("data").GetProperty("contract");
        Assert.Equal("completed", ready.GetProperty("status").GetString());
        Assert.True(ready.GetProperty("hasSignedDocument").GetBoolean());

        // O PDF assinado sai pela API.
        var pdf = await api.GetAsync($"/api/public/v1/contracts/{id}/signed-document");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);

        var deliveries = await (await admin.Client.GetAsync("/api/v1/integrations/deliveries")).JsonAsync();
        Assert.All(deliveries.GetProperty("items").EnumerateArray(), d => Assert.Equal("succeeded", d.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Failing_endpoints_are_retried_then_admins_are_warned_once()
    {
        var (admin, _, _) = await SetupAsync(factory, this);
        var url = Url();
        factory.Webhooks.RespondWith(url, HttpStatusCode.InternalServerError);
        var endpointId = (await CreateWebhookAsync(admin, url)).GetProperty("endpoint").GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.PostAsync($"/api/v1/integrations/webhooks/{endpointId}/test")).StatusCode);
        await factory.ProcessBackgroundAsync();
        await factory.ProcessBackgroundAsync();
        await factory.ProcessBackgroundAsync();
        await factory.ProcessBackgroundAsync(); // e-mail do aviso (outbox)

        Assert.Equal(3, factory.Webhooks.To(url).Count); // 1ª + 2 retentativas (configuradas no teste)
        Assert.Equal("webhook.test", factory.Webhooks.To(url)[0].Header("X-Contratos-Event"));
        var delivery = (await (await admin.Client.GetAsync($"/api/v1/integrations/deliveries?endpointId={endpointId}")).JsonAsync())
            .GetProperty("items")[0];
        Assert.Equal("failed", delivery.GetProperty("status").GetString());
        Assert.Equal(3, delivery.GetProperty("attempts").GetInt32());
        Assert.Equal(500, delivery.GetProperty("lastStatusCode").GetInt32());
        Assert.Equal("erro do servidor do cliente", delivery.GetProperty("responseBody").GetString());
        Assert.Single(factory.Emails.To(admin.Email), m => m.Subject.Contains("Webhook sem resposta", StringComparison.Ordinal));

        // Corrigido do lado do cliente: entregar de novo.
        factory.Webhooks.RespondWith(url, HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.Client.PostAsync($"/api/v1/integrations/deliveries/{delivery.GetProperty("id").GetInt64()}/redeliver")).StatusCode);
        await factory.ProcessBackgroundAsync();
        Assert.Equal(4, factory.Webhooks.To(url).Count);
        var overview = await (await admin.Client.GetAsync("/api/v1/integrations")).JsonAsync();
        Assert.Equal(0, overview.GetProperty("endpoints")[0].GetProperty("failureStreak").GetInt32());
    }

    [Fact]
    public async Task Disabled_endpoints_receive_nothing_and_the_secret_can_be_rotated()
    {
        var (admin, api, templateId) = await SetupAsync(factory, this);
        var url = Url();
        var created = await CreateWebhookAsync(admin, url);
        var endpointId = created.GetProperty("endpoint").GetProperty("id").GetGuid();
        var oldSecret = created.GetProperty("secret").GetString();

        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PutAsync($"/api/v1/integrations/webhooks/{endpointId}",
            new { url, events = Array.Empty<string>(), enabled = false })).StatusCode);
        await api.PostAsJsonAsync("/api/public/v1/contracts", NewContract(templateId));
        await factory.ProcessBackgroundAsync();
        Assert.Empty(factory.Webhooks.To(url));

        var rotated = await (await admin.Client.PostAsync($"/api/v1/integrations/webhooks/{endpointId}/secret")).JsonAsync();
        Assert.NotEqual(oldSecret, rotated.GetProperty("secret").GetString());
        var shown = await (await admin.Client.GetAsync($"/api/v1/integrations/webhooks/{endpointId}/secret")).JsonAsync();
        Assert.Equal(rotated.GetProperty("secret").GetString(), shown.GetProperty("secret").GetString());
    }

    [Theory]
    [InlineData("http://crm.exemplo.com.br/hook")]
    [InlineData("https://localhost/hook")]
    [InlineData("https://10.0.0.5/hook")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://[::1]/hook")]
    [InlineData("https://usuario:senha@crm.exemplo.com.br/hook")]
    [InlineData("crm.exemplo.com.br/hook")]
    public async Task Webhook_urls_must_be_public_https(string url)
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var res = await admin.Client.PostAsync("/api/v1/integrations/webhooks", new { url });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await res.JsonAsync()).GetProperty("errors").TryGetProperty("url", out _));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.20.30.40", true)]
    [InlineData("172.20.0.3", true)]
    [InlineData("192.168.1.10", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("100.100.1.1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("2606:4700:4700::1111", false)]
    public void Private_addresses_are_blocked_at_connect_time(string ip, bool blocked) =>
        Assert.Equal(blocked, WebhookUrls.IsPrivate(IPAddress.Parse(ip)));

    [Fact]
    public async Task Unknown_event_types_are_refused()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var res = await admin.Client.PostAsync("/api/v1/integrations/webhooks", new { url = Url(), events = new[] { "contract.exploded" } });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}

/// <summary>A documentação pública (portal e OpenAPI) descreve exatamente as rotas que existem.</summary>
[Collection(ApiCollection.Name)]
public sealed class PublicApiDocsTests(ApiFactory factory)
{
    [Fact]
    public async Task OpenApi_lists_every_public_route_and_nothing_else()
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var spec = await (await http.GetAsync("/desenvolvedores/openapi.json")).JsonAsync();
        var documented = spec.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject().Select(m => $"{m.Name.ToUpperInvariant()} {p.Name}"))
            .Order().ToList();

        const string prefix = "/api/public/v1";
        var routes = factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith(prefix, StringComparison.Ordinal) == true)
            .Where(e => e.RoutePattern.RawText!.Contains("{*", StringComparison.Ordinal) is false) // a rota de 404
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(m => $"{m} {System.Text.RegularExpressions.Regex.Replace(e.RoutePattern.RawText![prefix.Length..], ":[a-z]+}", "}")}"))
            .Order().ToList();

        Assert.Equal(routes, documented);
    }

    [Fact]
    public async Task Developer_portal_is_public_and_links_the_spec()
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var res = await http.GetAsync("/desenvolvedores/");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("/desenvolvedores/openapi.json", html);
        Assert.Contains("X-Contratos-Signature", html);
        Assert.DoesNotContain("style=\"", html); // a CSP bloqueia estilo inline
    }
}

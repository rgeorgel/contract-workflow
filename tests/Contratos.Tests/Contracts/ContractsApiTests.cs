using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Contratos.Domain.Contracts;
using Contratos.Infrastructure.Messaging;
using Contratos.Infrastructure.Persistence;
using Contratos.Infrastructure.Signatures;
using Contratos.Tests.Infrastructure;
using Contratos.Tests.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contratos.Tests.Contracts;

[Collection(ApiCollection.Name)]
public sealed partial class ContractsApiTests(ApiFactory factory)
{
    private static readonly object ValidValues = new Dictionary<string, string>
    {
        ["contratante.nome"] = "Café Aurora Ltda",
        ["contratante.cpf"] = "529.982.247-25",
        ["valor"] = "1500,50",
    };

    private async Task<(TestAccount Admin, Guid ContractId)> DraftAsync(string title = "Consultoria de marketing")
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var templateId = (await TemplatesApiTests.CreatePublishedAsync(admin)).GetProperty("id").GetGuid();
        var created = await admin.Client.PostAsync("/api/v1/contracts", new { templateId, title });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (admin, (await created.JsonAsync()).GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> ReadyToSendAsync(TestAccount admin, Guid id, string title = "Consultoria de marketing")
    {
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PutAsync($"/api/v1/contracts/{id}",
            new { title, variableValues = ValidValues, signingMode = "Sequential" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/v1/contracts/{id}/signers",
            new { name = "Marina Costa", email = "marina@cliente.com.br", deliveryMethod = "Email", role = "Contratante" })).StatusCode);
        var second = await admin.Client.PostAsync($"/api/v1/contracts/{id}/signers",
            new { name = "João Pereira", phone = "+55 (11) 98765-4321", deliveryMethod = "WhatsApp", role = "Contratado", cpf = "529.982.247-25" });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        return await second.JsonAsync();
    }

    private async Task<JsonElement> GetAsync(TestAccount admin, Guid id) =>
        await (await admin.Client.GetAsync($"/api/v1/contracts/{id}")).JsonAsync();

    private static Guid SignerId(JsonElement contract, int index) =>
        contract.GetProperty("signers")[index].GetProperty("id").GetGuid();

    private Task<HttpResponseMessage> SimulateAsync(TestAccount admin, Guid id, Guid signerId, string action) =>
        admin.Client.PostAsync($"/api/v1/dev/fake-provider/contracts/{id}/signers/{signerId}/{action}");

    [Fact]
    public async Task Full_flow_from_draft_to_signed_pdf()
    {
        var (admin, id) = await DraftAsync();
        var draft = await ReadyToSendAsync(admin, id);
        Assert.Equal("+5511987654321", draft.GetProperty("signers")[1].GetProperty("phone").GetString());

        var send = await admin.Client.SendAsync(HttpMethod.Post, $"/api/v1/contracts/{id}/send", null);
        Assert.Equal(HttpStatusCode.Accepted, send.StatusCode);
        var sent = await send.JsonAsync();
        Assert.Equal("Sent", sent.GetProperty("status").GetString());
        Assert.Equal(64, sent.GetProperty("documentSha256").GetString()!.Length);

        // O PDF congelado tem os valores formatados.
        var pdf = await (await admin.Client.GetAsync($"/api/v1/contracts/{id}/document")).Content.ReadAsStringAsync();
        Assert.Contains("Café Aurora Ltda", pdf);
        Assert.Contains("1.500,50", pdf);

        await factory.ProcessBackgroundAsync();
        var delivered = await GetAsync(admin, id);
        Assert.True(delivered.GetProperty("permissions").GetProperty("canSimulate").GetBoolean());

        var first = SignerId(delivered, 0);
        var second = SignerId(delivered, 1);
        Assert.Equal(HttpStatusCode.NoContent, (await SimulateAsync(admin, id, first, "view")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SimulateAsync(admin, id, first, "sign")).StatusCode);
        var partial = await GetAsync(admin, id);
        Assert.Equal("PartiallySigned", partial.GetProperty("status").GetString());
        Assert.Equal("Signed", partial.GetProperty("signers")[0].GetProperty("status").GetString());

        await SimulateAsync(admin, id, second, "sign");
        await factory.ProcessBackgroundAsync(); // baixa o PDF assinado
        var done = await GetAsync(admin, id);
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        Assert.True(done.GetProperty("hasSignedDocument").GetBoolean());
        var signed = await admin.Client.GetAsync($"/api/v1/contracts/{id}/signed-document");
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        Assert.Equal("application/pdf", signed.Content.Headers.ContentType?.MediaType);

        var events = (await (await admin.Client.GetAsync($"/api/v1/contracts/{id}/events")).JsonAsync())
            .EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToList();
        foreach (var type in new[] { "Created", "SignerAdded", "ContractSent", "SentToProvider", "SignerViewed", "SignerSigned", "Completed", "SignedDocumentStored" })
            Assert.Contains(type, events);

        var usage = (await (await admin.Client.GetAsync("/api/v1/organization/settings")).JsonAsync()).GetProperty("usage");
        Assert.Equal(1, usage.GetProperty("contractsSent").GetInt32());
    }

    [Fact]
    public async Task Expiry_date_is_saved_as_the_end_of_that_day_in_brasilia()
    {
        var (admin, id) = await DraftAsync();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));

        var res = await admin.Client.PutAsync($"/api/v1/contracts/{id}", new { title = "Com prazo", expiresAt = date.ToString("yyyy-MM-dd") });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var expires = (await res.JsonAsync()).GetProperty("expiresAt").GetDateTimeOffset();
        Assert.Equal(new DateTimeOffset(date.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.FromHours(-3)), expires);
    }

    [Fact]
    public async Task Send_is_idempotent_with_the_same_key()
    {
        var (admin, id) = await DraftAsync();
        await ReadyToSendAsync(admin, id);

        async Task<HttpResponseMessage> Send(string key)
        {
            var csrf = await admin.Client.Http.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/contracts/{id}/send");
            req.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
            req.Headers.Add("Idempotency-Key", key);
            return await admin.Client.Http.SendAsync(req);
        }

        Assert.Equal(HttpStatusCode.Accepted, (await Send("clique-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Send("clique-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send("outra-chave")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.OutboxMessages.FromSql($"SELECT * FROM outbox_messages WHERE payload->>'contractId' = {id.ToString()}").ToListAsync());
    }

    [Fact]
    public async Task Cannot_send_with_missing_values_or_without_signers()
    {
        var (admin, id) = await DraftAsync();

        var res = await admin.Client.PostAsync($"/api/v1/contracts/{id}/send");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await res.JsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("signers", out _));
        Assert.True(errors.TryGetProperty("variableValues.contratante.nome", out _));
    }

    [Theory]
    [InlineData("WhatsApp", null, null, "phone")]
    [InlineData("Sms", null, "11987654321x", "phone")]
    [InlineData("Email", null, null, "email")]
    public async Task Signer_channel_needs_its_contact(string method, string? email, string? phone, string field)
    {
        var (admin, id) = await DraftAsync();

        var res = await admin.Client.PostAsync($"/api/v1/contracts/{id}/signers", new { name = "Fulano", email, phone, deliveryMethod = method });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await res.JsonAsync()).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Invalid_variable_values_are_rejected_by_type()
    {
        var (admin, id) = await DraftAsync();

        var res = await admin.Client.PutAsync($"/api/v1/contracts/{id}", new
        {
            title = "X",
            variableValues = new Dictionary<string, string> { ["contratante.cpf"] = "123", ["valor"] = "muito" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await res.JsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("variableValues.contratante.cpf", out _));
        Assert.True(errors.TryGetProperty("variableValues.valor", out _));
    }

    [Fact]
    public async Task Sent_contract_is_frozen_and_can_be_cancelled()
    {
        var (admin, id) = await DraftAsync();
        await ReadyToSendAsync(admin, id);
        await admin.Client.PostAsync($"/api/v1/contracts/{id}/send");
        await factory.ProcessBackgroundAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.PutAsync($"/api/v1/contracts/{id}", new { title = "Mudou" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.PostAsync($"/api/v1/contracts/{id}/signers", new { name = "Novo", email = "novo@x.com.br" })).StatusCode);

        var cancel = await admin.Client.PostAsync($"/api/v1/contracts/{id}/cancel");
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("Cancelled", (await cancel.JsonAsync()).GetProperty("status").GetString());

        // A remoção no provedor sai pela outbox.
        await factory.ProcessBackgroundAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.OutboxMessages.AnyAsync(m => m.Type == OutboxTypes.DeleteProviderDocument && m.ProcessedAt != null && m.LastError == null));
    }

    [Fact]
    public async Task Declined_signature_declines_the_contract()
    {
        var (admin, id) = await DraftAsync();
        await ReadyToSendAsync(admin, id);
        await admin.Client.PostAsync($"/api/v1/contracts/{id}/send");
        await factory.ProcessBackgroundAsync();
        var signer = SignerId(await GetAsync(admin, id), 1);

        await SimulateAsync(admin, id, signer, "decline");

        var contract = await GetAsync(admin, id);
        Assert.Equal("Declined", contract.GetProperty("status").GetString());
        Assert.Equal("Recusa simulada", contract.GetProperty("signers")[1].GetProperty("declineReason").GetString());
    }

    [Fact]
    public async Task Provider_failure_is_retried_and_then_shown_as_send_error()
    {
        var title = $"Contrato {FakeSignatureProvider.FailMarker}";
        var (admin, id) = await DraftAsync(title);
        await ReadyToSendAsync(admin, id, title);
        await admin.Client.PostAsync($"/api/v1/contracts/{id}/send");

        await factory.ProcessBackgroundAsync();

        var contract = await GetAsync(admin, id);
        Assert.Equal("Sent", contract.GetProperty("status").GetString());
        Assert.Contains("Falha simulada", contract.GetProperty("sendError").ToString());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = (await db.OutboxMessages.FromSql($"SELECT * FROM outbox_messages WHERE payload->>'contractId' = {id.ToString()}").ToListAsync()).Single();
        Assert.Equal(5, message.Attempts);
    }

    [Fact]
    public async Task Webhook_requires_a_valid_signature_and_ignores_repeated_events()
    {
        var (admin, id) = await DraftAsync();
        await ReadyToSendAsync(admin, id);
        await admin.Client.PostAsync($"/api/v1/contracts/{id}/send");
        await factory.ProcessBackgroundAsync();

        string documentId, publicId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Contracts.IgnoreQueryFilters().Include(x => x.Signers).SingleAsync(x => x.Id == id);
            (documentId, publicId) = (c.AutentiqueDocumentId!, c.SignersInSendOrder()[0].AutentiquePublicId!);
        }
        var fake = factory.Services.GetRequiredService<FakeSignatureProvider>();
        var (body, signature) = fake.BuildWebhook(new(
            "evento-repetido-" + id, Contratos.Application.Signatures.SignatureEventType.Viewed, documentId, publicId, null, DateTimeOffset.UtcNow));

        var anon = factory.CreateApiClient().Http;
        HttpRequestMessage Webhook(string sig) => new(HttpMethod.Post, "/api/v1/webhooks/autentique")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            Headers = { { "x-autentique-signature", sig } },
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(Webhook("assinatura-falsa"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.SendAsync(Webhook(signature))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.SendAsync(Webhook(signature))).StatusCode);
        await factory.ProcessBackgroundAsync();

        var viewed = (await (await admin.Client.GetAsync($"/api/v1/contracts/{id}/events")).JsonAsync())
            .EnumerateArray().Count(e => e.GetProperty("type").GetString() == "SignerViewed");
        Assert.Equal(1, viewed);
    }

    [Fact]
    public async Task Expired_contracts_are_closed_by_the_worker()
    {
        var (admin, id) = await DraftAsync();
        await ReadyToSendAsync(admin, id);
        await admin.Client.PostAsync($"/api/v1/contracts/{id}/send");
        await factory.ProcessBackgroundAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlAsync($"UPDATE contracts SET expires_at = now() - interval '1 minute' WHERE id = {id}");
        }

        await factory.ProcessBackgroundAsync();

        Assert.Equal("Expired", (await GetAsync(admin, id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Deleting_requires_the_number_and_keeps_a_minimal_log()
    {
        var (admin, id) = await DraftAsync();
        await ReadyToSendAsync(admin, id);
        await admin.Client.PostAsync($"/api/v1/contracts/{id}/send");
        await factory.ProcessBackgroundAsync();
        var number = (await GetAsync(admin, id)).GetProperty("number").GetInt32();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.DeleteAsync($"/api/v1/contracts/{id}?confirmNumber={number + 1}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.DeleteAsync($"/api/v1/contracts/{id}?confirmNumber={number}&alsoInAutentique=true")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.Client.GetAsync($"/api/v1/contracts/{id}")).StatusCode);
        Assert.DoesNotContain(factory.Files.Keys, k => k.Contains(id.ToString()));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.DeletionLog.SingleAsync(d => d.ContractNumber == number && d.ContractTitle == "Consultoria de marketing" && d.DeletedInAutentique);
        Assert.NotNull(log.SentAt);
        // O envio continua contando no uso do mês.
        var usage = (await (await admin.Client.GetAsync("/api/v1/organization/settings")).JsonAsync()).GetProperty("usage");
        Assert.Equal(1, usage.GetProperty("contractsSent").GetInt32());
    }

    [Fact]
    public async Task Numbers_are_sequential_per_organization()
    {
        var (admin, first) = await DraftAsync();
        var templateId = (await GetAsync(admin, first)).GetProperty("template").GetProperty("id").GetGuid();
        var second = await (await admin.Client.PostAsync("/api/v1/contracts", new { templateId })).JsonAsync();

        Assert.Equal(1, (await GetAsync(admin, first)).GetProperty("number").GetInt32());
        Assert.Equal(2, second.GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task Contracts_of_another_organization_are_invisible()
    {
        var (adminA, id) = await DraftAsync();
        await ReadyToSendAsync(adminA, id);
        await adminA.Client.PostAsync($"/api/v1/contracts/{id}/send");
        var adminB = await TestAccounts.CreateAdminAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await adminB.Client.GetAsync($"/api/v1/contracts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.Client.GetAsync($"/api/v1/contracts/{id}/document")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.Client.GetAsync($"/api/v1/contracts/{id}/events")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.Client.PostAsync($"/api/v1/contracts/{id}/cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.Client.DeleteAsync($"/api/v1/contracts/{id}?confirmNumber=1")).StatusCode);
        // Total e itens vêm de consultas diferentes: os dois precisam respeitar a organização.
        await AssertListIsOnlyMineAsync(adminB, expected: 0);

        // E no sentido contrário: com contratos nas duas organizações, cada uma só vê os seus.
        var templateB = (await TemplatesApiTests.CreatePublishedAsync(adminB, "Template B")).GetProperty("id").GetGuid();
        await adminB.Client.PostAsync("/api/v1/contracts", new { templateId = templateB, title = "Contrato de B" });
        await AssertListIsOnlyMineAsync(adminA, expected: 1);
        await AssertListIsOnlyMineAsync(adminB, expected: 1);
        foreach (var q in new[] { "contrato", "1", "marina" })
        {
            var search = await (await adminB.Client.GetAsync($"/api/v1/contracts?q={q}")).JsonAsync();
            Assert.DoesNotContain(search.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == id);
        }
    }

    private static async Task AssertListIsOnlyMineAsync(TestAccount account, int expected)
    {
        var list = await (await account.Client.GetAsync("/api/v1/contracts?pageSize=100")).JsonAsync();
        Assert.Equal(expected, list.GetProperty("total").GetInt32());
        Assert.Equal(expected, list.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Readers_only_read()
    {
        var (admin, id) = await DraftAsync();
        var reader = await TestAccounts.InviteAndAcceptAsync(factory, admin, "Leitor");
        var templateId = (await GetAsync(admin, id)).GetProperty("template").GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await reader.Client.GetAsync($"/api/v1/contracts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.Client.PostAsync("/api/v1/contracts", new { templateId })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.Client.PostAsync($"/api/v1/contracts/{id}/send")).StatusCode);
    }

    [Fact]
    public async Task Search_finds_by_signer_and_filters_by_status()
    {
        var (admin, id) = await DraftAsync("Locação comercial");
        await ReadyToSendAsync(admin, id, "Locação comercial");

        var bySigner = await (await admin.Client.GetAsync("/api/v1/contracts?q=marina@cliente")).JsonAsync();
        Assert.Equal(1, bySigner.GetProperty("total").GetInt32());
        var byStatus = await (await admin.Client.GetAsync("/api/v1/contracts?status=Sent")).JsonAsync();
        Assert.Equal(0, byStatus.GetProperty("total").GetInt32());
        var item = (await (await admin.Client.GetAsync("/api/v1/contracts?q=loca")).JsonAsync()).GetProperty("items")[0];
        Assert.Equal(2, item.GetProperty("signersTotal").GetInt32());
        Assert.Equal("Prestação de serviços", item.GetProperty("templateName").GetString());
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contratos.Domain.Contracts;
using Contratos.Infrastructure.Persistence;
using Contratos.Tests.Infrastructure;
using Contratos.Tests.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contratos.Tests.Contracts;

/// <summary>Fluxo completo com o provedor Autentique: GraphQL, webhooks assinados, PDF assinado e reconciliação.</summary>
[Collection(AutentiqueCollection.Name)]
public sealed class AutentiqueFlowTests(AutentiqueApiFactory factory)
{
    private FakeAutentique Autentique => factory.Autentique;

    private async Task<(TestAccount Admin, Guid Id)> SentAsync(string title = "Prestação de serviços")
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var templateId = (await TemplatesApiTests.CreatePublishedAsync(admin)).GetProperty("id").GetGuid();
        var id = (await (await admin.Client.PostAsync("/api/v1/contracts", new { templateId, title })).JsonAsync()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PutAsync($"/api/v1/contracts/{id}", new
        {
            title,
            variableValues = new Dictionary<string, string> { ["contratante.nome"] = "Café Aurora Ltda", ["contratante.cpf"] = "529.982.247-25", ["valor"] = "1500,50" },
            signingMode = "Sequential",
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/v1/contracts/{id}/signers",
            new { name = "Marina Costa", email = "marina@cliente.com.br", deliveryMethod = "Email", role = "Contratante" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/v1/contracts/{id}/signers",
            new { name = "João Pereira", phone = "+55 (11) 98765-4321", deliveryMethod = "WhatsApp", role = "Contratado", cpf = "529.982.247-25" })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await admin.Client.SendAsync(HttpMethod.Post, $"/api/v1/contracts/{id}/send", null)).StatusCode);
        await factory.ProcessBackgroundAsync();
        return (admin, id);
    }

    private async Task<Contract> LoadAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Contracts.IgnoreQueryFilters().Include(c => c.Signers).AsNoTracking().SingleAsync(c => c.Id == id);
    }

    private async Task<int> EventCountAsync(Guid id, string type)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ContractEvents.IgnoreQueryFilters().CountAsync(e => e.ContractId == id && e.Type == type);
    }

    private async Task<HttpStatusCode> PostWebhookAsync(string body, string signature)
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/autentique")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Autentique-Signature", signature);
        return (await client.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task Sends_the_document_with_signers_mapped_to_autentique_fields()
    {
        var (admin, id) = await SentAsync();

        var contract = await LoadAsync(id);
        Assert.NotNull(contract.AutentiqueDocumentId);
        var create = Autentique.Mutations("createDocument").Last(r => r.Variables!["document"]!["name"]!.GetValue<string>().StartsWith("Prestação", StringComparison.Ordinal));
        Assert.Equal($"Bearer {FakeAutentique.ApiKey}", create.Authorization);
        Assert.Contains("sandbox: true", create.Query);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(create.File!, 0, 4));

        var document = create.Variables!["document"]!;
        Assert.True(document["sortable"]!.GetValue<bool>());
        Assert.True(document["refusable"]!.GetValue<bool>());
        Assert.Equal("pt-BR", document["locale"]!["language"]!.GetValue<string>());
        Assert.Contains("enviou este documento", document["message"]!.GetValue<string>());

        var signers = create.Variables["signers"]!.AsArray();
        Assert.Equal("marina@cliente.com.br", signers[0]!["email"]!.GetValue<string>());
        Assert.Equal("SIGN", signers[0]!["action"]!.GetValue<string>());
        Assert.Null(signers[0]!["phone"]);
        Assert.Equal("+5511987654321", signers[1]!["phone"]!.GetValue<string>());
        Assert.Equal("DELIVERY_METHOD_WHATSAPP", signers[1]!["delivery_method"]!.GetValue<string>());
        Assert.Equal("52998224725", signers[1]!["configs"]!["cpf"]!.GetValue<string>());

        // A Autentique devolveu as assinaturas fora de ordem e com o dono da conta junto: o vínculo é pelo contato.
        var ids = Autentique.Documents[contract.AutentiqueDocumentId!];
        var ordered = contract.SignersInSendOrder();
        Assert.Equal(ids[0], ordered[0].AutentiquePublicId);
        Assert.Equal(ids[1], ordered[1].AutentiquePublicId);
        Assert.StartsWith("https://assina.ae/", ordered[0].ShortLink);

        // Sem simulador com o provedor real.
        var detail = await (await admin.Client.GetAsync($"/api/v1/contracts/{id}")).JsonAsync();
        Assert.False(detail.GetProperty("permissions").GetProperty("canSimulate").GetBoolean());
    }

    [Fact]
    public async Task Signed_webhooks_drive_the_contract_to_completed_and_archive_the_signed_pdf()
    {
        var (admin, id) = await SentAsync();
        var docId = (await LoadAsync(id)).AutentiqueDocumentId!;
        var ids = Autentique.Documents[docId];

        // Assinatura inválida: recusado, nada gravado.
        var (viewed, viewedSig) = FakeAutentique.Webhook("signature.viewed", new JsonObject { ["public_id"] = FakeAutentique.Dashed(ids[0]), ["document"] = docId });
        Assert.Equal(HttpStatusCode.Unauthorized, await PostWebhookAsync(viewed, FakeAutentique.Sign(viewed, "outro-secret")));
        Assert.Equal(HttpStatusCode.Unauthorized, await PostWebhookAsync(viewed, ""));

        // Válido e reentregue: aplicado uma vez só.
        Assert.Equal(HttpStatusCode.OK, await PostWebhookAsync(viewed, viewedSig));
        Assert.Equal(HttpStatusCode.OK, await PostWebhookAsync(viewed, viewedSig.ToUpperInvariant()));
        await factory.ProcessBackgroundAsync();
        Assert.Equal(1, await EventCountAsync(id, ContractEventTypes.SignerViewed));

        // Formato alternativo da documentação: recurso direto em data, documento como objeto.
        // Assinado com o secret do segundo endpoint (o de eventos de assinatura).
        var (signed1, _) = FakeAutentique.Webhook("signature.accepted",
            new JsonObject { ["public_id"] = FakeAutentique.Dashed(ids[0]), ["document"] = new JsonObject { ["id"] = docId } }, resourceUnderObject: false);
        Assert.Equal(HttpStatusCode.OK, await PostWebhookAsync(signed1, FakeAutentique.Sign(signed1, FakeAutentique.SecondWebhookSecret)));
        // Sem o documento no evento: o contrato é achado pela assinatura.
        var (signed2, sig2) = FakeAutentique.Webhook("signature.accepted", new JsonObject { ["public_id"] = FakeAutentique.Dashed(ids[1]) });
        Assert.Equal(HttpStatusCode.OK, await PostWebhookAsync(signed2, sig2));
        // Evento que o sistema não usa: aceito e ignorado.
        var (updated, updatedSig) = FakeAutentique.Webhook("document.updated", new JsonObject { ["id"] = docId });
        Assert.Equal(HttpStatusCode.OK, await PostWebhookAsync(updated, updatedSig));
        var (finished, finishedSig) = FakeAutentique.Webhook("document.finished", new JsonObject { ["id"] = docId });
        Assert.Equal(HttpStatusCode.OK, await PostWebhookAsync(finished, finishedSig));

        await factory.ProcessBackgroundAsync(); // eventos + guarda do PDF assinado

        var contract = await LoadAsync(id);
        Assert.Equal(ContractStatus.Completed, contract.Status);
        Assert.All(contract.Signers, s => Assert.Equal(SignerStatus.Signed, s.Status));
        Assert.NotNull(contract.SignedDocumentKey);

        var pdf = await (await admin.Client.GetAsync($"/api/v1/contracts/{id}/signed-document")).Content.ReadAsByteArrayAsync();
        Assert.Equal(FakeAutentique.SignedPdf, pdf);
        // O link do arquivo não é da Autentique: o token da API não vai junto.
        Assert.Null(Autentique.Requests.Last(r => r.Url == FakeAutentique.SignedPdfUrl).Authorization);
    }

    [Fact]
    public async Task Cancelling_blocks_signing_and_deletes_the_document_in_autentique()
    {
        var (admin, id) = await SentAsync();
        var docId = (await LoadAsync(id)).AutentiqueDocumentId!;

        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/v1/contracts/{id}/cancel")).StatusCode);
        await factory.ProcessBackgroundAsync();

        var block = Autentique.Mutations("updateDocument").Last(r => r.Variables!["id"]!.GetValue<string>() == docId);
        var deadline = DateTimeOffset.Parse(block.Variables!["document"]!["deadline_at"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(deadline <= DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.Contains(Autentique.Mutations("deleteDocument"), r => r.Query!.Contains(docId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reconciliation_applies_what_the_webhooks_missed()
    {
        var (admin, id) = await SentAsync();
        var docId = (await LoadAsync(id)).AutentiqueDocumentId!;
        foreach (var publicId in Autentique.Documents[docId])
            Autentique.SignatureState[publicId] = (DateTimeOffset.UtcNow.AddHours(-30), DateTimeOffset.UtcNow.AddHours(-29));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlAsync($"UPDATE contracts SET sent_at = now() - interval '2 days' WHERE id = {id}");
        }

        await factory.ProcessBackgroundAsync(); // consulta e aplica
        await factory.ProcessBackgroundAsync(); // guarda o PDF assinado

        var contract = await LoadAsync(id);
        Assert.Equal(ContractStatus.Completed, contract.Status);
        Assert.NotNull(contract.ProviderCheckedAt);
        Assert.NotNull(contract.SignedDocumentKey);
        Assert.Equal(2, await EventCountAsync(id, ContractEventTypes.SignerSigned));

        // Já consultado e concluído: não volta a perguntar.
        bool IsStatusQuery(FakeAutentique.Captured r) => r.Query?.Contains(docId, StringComparison.Ordinal) == true && r.Query.Contains("rejected", StringComparison.Ordinal);
        var queries = Autentique.Requests.Count(IsStatusQuery);
        await factory.ProcessBackgroundAsync();
        Assert.Equal(queries, Autentique.Requests.Count(IsStatusQuery));
    }

    [Fact]
    public async Task Rate_limit_is_retried_and_out_of_credits_ends_as_send_error()
    {
        var before = Autentique.Mutations("createDocument").Count();
        Autentique.FailNext = () => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("""{"message":"Too Many Attempts."}""") };
        var (_, id) = await SentAsync("Com limite");
        Assert.NotNull((await LoadAsync(id)).AutentiqueDocumentId); // a outbox tentou de novo (espera 0 nos testes)
        Assert.Equal(before + 2, Autentique.Mutations("createDocument").Count());

        var admin = await TestAccounts.CreateAdminAsync(factory);
        var templateId = (await TemplatesApiTests.CreatePublishedAsync(admin)).GetProperty("id").GetGuid();
        var draftId = (await (await admin.Client.PostAsync("/api/v1/contracts", new { templateId, title = "Sem créditos" })).JsonAsync()).GetProperty("id").GetGuid();
        await admin.Client.PutAsync($"/api/v1/contracts/{draftId}", new
        {
            title = "Sem créditos",
            variableValues = new Dictionary<string, string> { ["contratante.nome"] = "X", ["contratante.cpf"] = "529.982.247-25", ["valor"] = "10" },
        });
        await admin.Client.PostAsync($"/api/v1/contracts/{draftId}/signers", new { name = "Ana", email = "ana@x.com.br", deliveryMethod = "Email", role = "Parte" });
        Assert.Equal(HttpStatusCode.Accepted, (await admin.Client.SendAsync(HttpMethod.Post, $"/api/v1/contracts/{draftId}/send", null)).StatusCode);
        Autentique.FailCreateDocument = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"errors":[{"message":"unavailable_credits"}],"data":{"createDocument":null}}""", Encoding.UTF8, "application/json"),
        };
        try
        {
            for (var i = 0; i < 5; i++)
                await factory.ProcessBackgroundAsync();
        }
        finally
        {
            Autentique.FailCreateDocument = null;
        }

        var failed = await (await admin.Client.GetAsync($"/api/v1/contracts/{draftId}")).JsonAsync();
        Assert.Contains("sem créditos", failed.GetProperty("sendError").GetString());
    }
}

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Contratos.Application.Signatures;
using Contratos.Infrastructure.Signatures;
using Contratos.Tests.Infrastructure;
using Contratos.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Contratos.Tests.Contracts;

public sealed class AutentiqueProviderTests
{
    private static AutentiqueSignatureProvider Provider(HttpMessageHandler? handler = null, string? secret = FakeAutentique.WebhookSecret) => new(
        new HttpClient(handler ?? new FakeAutentique()),
        Options.Create(new AutentiqueOptions { ApiKey = FakeAutentique.ApiKey, WebhookSecret = secret, ApiUrl = "https://api.autentique.test/v2/graphql" }),
        TimeProvider.System,
        NullLogger<AutentiqueSignatureProvider>.Instance);

    private sealed class Respond(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Seen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // ---------- HMAC ----------

    [Fact]
    public void Webhook_signature_is_hmac_sha256_hex_of_the_raw_body()
    {
        var provider = Provider();
        const string body = """{"event":{"id":"1","type":"document.finished","data":{"object":{"id":"d"}}}}""";
        var signature = FakeAutentique.Sign(body);

        Assert.True(provider.ValidateWebhook(body, signature));
        Assert.True(provider.ValidateWebhook(body, signature.ToUpperInvariant()));
        Assert.True(provider.ValidateWebhook(body, $"sha256={signature}"));
        Assert.False(provider.ValidateWebhook(body + " ", signature)); // corpo bruto: um espaço muda tudo
        Assert.False(provider.ValidateWebhook(body, FakeAutentique.Sign(body, "outro")));
        Assert.False(provider.ValidateWebhook(body, null));
        Assert.False(provider.ValidateWebhook(body, "zz"));
    }

    [Fact]
    public void Each_endpoint_secret_in_the_list_is_accepted()
    {
        var provider = Provider(secret: " primeiro , segundo ,");
        const string body = "{}";

        Assert.True(provider.ValidateWebhook(body, FakeAutentique.Sign(body, "primeiro")));
        Assert.True(provider.ValidateWebhook(body, FakeAutentique.Sign(body, "segundo")));
        Assert.False(provider.ValidateWebhook(body, FakeAutentique.Sign(body, "terceiro")));
        Assert.False(provider.ValidateWebhook(body, FakeAutentique.Sign(body, "primeiro , segundo")));
    }

    [Fact]
    public void Without_a_configured_secret_every_webhook_is_rejected()
    {
        const string body = "{}";
        Assert.False(Provider(secret: "").ValidateWebhook(body, FakeAutentique.Sign(body, "")));
    }

    // ---------- Formato do webhook ----------

    [Theory]
    [InlineData("signature.viewed", SignatureEventType.Viewed)]
    [InlineData("signature.accepted", SignatureEventType.Signed)]
    [InlineData("signature.rejected", SignatureEventType.Declined)]
    [InlineData("signature.delivery_failed", SignatureEventType.DeliveryFailed)]
    public void Signature_events_in_both_documented_shapes(string type, SignatureEventType expected)
    {
        var resource = new JsonObject
        {
            ["public_id"] = "sig-1",
            ["document"] = "doc-1",
            ["rejected"] = type == "signature.rejected" ? new JsonObject { ["reason"] = "Valor errado", ["created_at"] = "2026-11-15 10:00:00" } : null,
        };
        foreach (var underObject in new[] { true, false })
        {
            var (body, _) = FakeAutentique.Webhook(type, (JsonObject)resource.DeepClone(), underObject, eventId: "evt-1");
            var ev = Assert.Single(Provider().ParseWebhook(body));
            Assert.Equal(("evt-1", expected, "doc-1", "sig-1"), (ev.ExternalEventId, ev.Type, ev.DocumentId, ev.SignaturePublicId));
            Assert.Equal(new DateTimeOffset(2026, 11, 15, 13, 45, 10, TimeSpan.Zero), ev.OccurredAt);
            if (expected == SignatureEventType.Declined)
                Assert.Equal("Valor errado", ev.Reason);
        }
    }

    [Fact]
    public void Document_events_and_ignored_types()
    {
        var provider = Provider();
        var (finished, _) = FakeAutentique.Webhook("document.finished", new JsonObject { ["id"] = "doc-9" });
        var ev = Assert.Single(provider.ParseWebhook(finished));
        Assert.Equal((SignatureEventType.DocumentFinished, "doc-9", (string?)null), (ev.Type, ev.DocumentId, ev.SignaturePublicId));

        var (deleted, _) = FakeAutentique.Webhook("document.deleted", new JsonObject { ["id"] = "doc-9" });
        Assert.Equal(SignatureEventType.DocumentDeleted, Assert.Single(provider.ParseWebhook(deleted)).Type);

        foreach (var type in new[] { "document.created", "document.updated", "signature.created", "member.created" })
            Assert.Empty(provider.ParseWebhook(FakeAutentique.Webhook(type, new JsonObject { ["id"] = "x", ["public_id"] = "y" }).Body));
    }

    [Fact]
    public void Document_given_as_object_or_missing()
    {
        var provider = Provider();
        var asObject = FakeAutentique.Webhook("signature.accepted", new JsonObject { ["public_id"] = "s", ["document"] = new JsonObject { ["id"] = "doc-2" } }).Body;
        Assert.Equal("doc-2", Assert.Single(provider.ParseWebhook(asObject)).DocumentId);

        var missing = FakeAutentique.Webhook("signature.accepted", new JsonObject { ["public_id"] = "s" }).Body;
        Assert.Equal("", Assert.Single(provider.ParseWebhook(missing)).DocumentId); // o processador acha pela assinatura
    }

    [Theory]
    [InlineData("""{"event":{"type":"document.finished","data":{"object":{"id":"d"}}}}""")]            // sem event.id
    [InlineData("""{"event":{"id":"1","type":"signature.accepted","data":{"object":{"document":"d"}}}}""")] // sem public_id
    [InlineData("""{"event":{"id":"1","type":"document.finished"}}""")]                                 // sem data
    public void Malformed_webhooks_are_rejected(string body) =>
        Assert.ThrowsAny<FormatException>(() => Provider().ParseWebhook(body));

    // ---------- Erros e chamadas ----------

    [Fact]
    public async Task Graphql_errors_become_exceptions_with_the_autentique_code()
    {
        var handler = new Respond(_ => Json("""{"errors":[{"message":"validation","extensions":{"validation":{"signers.0.email":["must_be_a_valid_email_address"]},"category":"validation"}}],"data":null}"""));

        var ex = await Assert.ThrowsAsync<AutentiqueException>(() => Provider(handler).ResendAsync(["s"], CancellationToken.None));

        Assert.Equal("validation", ex.Code);
        Assert.Contains("signers.0.email: must_be_a_valid_email_address", ex.Message);
    }

    [Fact]
    public async Task Rate_limit_and_bad_key_are_errors()
    {
        var limited = new Respond(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("""{"message":"Too Many Attempts."}""") });
        Assert.Equal("rate_limited", (await Assert.ThrowsAsync<AutentiqueException>(() => Provider(limited).ResendAsync(["s"], CancellationToken.None))).Code);

        var badKey = new Respond(_ => Json("""{"errors":[{"message":"unauthorized"}]}"""));
        Assert.Equal("unauthorized", (await Assert.ThrowsAsync<AutentiqueException>(() => Provider(badKey).ResendAsync(["s"], CancellationToken.None))).Code);
    }

    [Fact]
    public async Task Deleting_a_document_that_is_already_gone_is_fine()
    {
        var handler = new Respond(_ => Json("""{"errors":[{"message":"document_not_found"}],"data":null}"""));
        await Provider(handler).DeleteDocumentAsync("doc-x", CancellationToken.None);
        Assert.Equal(2, handler.Seen.Count); // prazo vencido + exclusão
    }

    [Fact]
    public async Task Resend_too_soon_is_not_an_error()
    {
        var handler = new Respond(_ => Json("""{"errors":[{"message":"too_many_resent_emails"}],"data":null}"""));
        await Provider(handler).ResendAsync(["s"], CancellationToken.None);
    }

    [Fact]
    public async Task Signed_pdf_from_autentique_host_gets_the_token_and_must_be_a_pdf()
    {
        const string url = "https://painel.autentique.com.br/documentos/doc-1/assinado.pdf";
        var handler = new Respond(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>login</html>") }
            : Json(new JsonObject { ["data"] = new JsonObject { ["document"] = new JsonObject { ["files"] = new JsonObject { ["signed"] = url } } } }.ToJsonString()));

        var ex = await Assert.ThrowsAsync<AutentiqueException>(() => Provider(handler).DownloadSignedPdfAsync("doc-1", CancellationToken.None));

        Assert.Equal("download_failed", ex.Code);
        var get = handler.Seen.Single(r => r.Method == HttpMethod.Get);
        Assert.Equal($"Bearer {FakeAutentique.ApiKey}", get.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task Signed_pdf_not_ready_yet_is_retried_later()
    {
        var handler = new Respond(_ => Json("""{"data":{"document":{"files":{"original":"https://x.test/o.pdf","signed":null}}}}"""));
        var ex = await Assert.ThrowsAsync<AutentiqueException>(() => Provider(handler).DownloadSignedPdfAsync("doc-1", CancellationToken.None));
        Assert.Equal("signed_file_unavailable", ex.Code);
    }

    [Fact]
    public async Task Sandbox_off_and_folder_go_into_the_mutation()
    {
        var fake = new FakeAutentique();
        var provider = new AutentiqueSignatureProvider(new HttpClient(fake),
            Options.Create(new AutentiqueOptions { ApiKey = FakeAutentique.ApiKey, WebhookSecret = "s", Sandbox = false }),
            TimeProvider.System, NullLogger<AutentiqueSignatureProvider>.Instance);

        var created = await provider.CreateDocumentAsync(
            new DocumentRequest("Contrato", null, false, null, "WEEKLY", "pasta-1",
                [new DocumentSigner("Ana", null, "+5521999990000", Contratos.Domain.Contracts.DeliveryMethod.Sms, Contratos.Domain.Templates.SignerAction.SignAsWitness, null)]),
            new MemoryStream("%PDF-1.4"u8.ToArray()), CancellationToken.None);

        var request = Assert.Single(fake.Requests);
        Assert.DoesNotContain("sandbox", request.Query);
        Assert.Contains("folder_id: \"pasta-1\"", request.Query);
        Assert.Equal("WEEKLY", request.Variables!["document"]!["reminder"]!.GetValue<string>());
        Assert.Null(request.Variables["document"]!["deadline_at"]);
        var signer = request.Variables["signers"]![0]!;
        Assert.Equal("DELIVERY_METHOD_SMS", signer["delivery_method"]!.GetValue<string>());
        Assert.Equal("SIGN_AS_A_WITNESS", signer["action"]!.GetValue<string>());
        Assert.Null(signer["configs"]);
        Assert.Equal(fake.Documents[created.DocumentId][0], Assert.Single(created.Signatures).PublicId);
    }

    [Theory]
    [InlineData(null, "s", "AUTENTIQUE_API_KEY")]
    [InlineData("k", null, "AUTENTIQUE_WEBHOOK_SECRET")]
    [InlineData("k", " , ", "AUTENTIQUE_WEBHOOK_SECRET")]
    public void Autentique_without_key_or_secret_fails_at_startup(string? key, string? secret, string missing)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=x;Username=x;Password=x",
            ["Signature:Provider"] = "Autentique",
            ["Autentique:ApiKey"] = key,
            ["Autentique:WebhookSecret"] = secret,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();

        var ex = Assert.Throws<InvalidOperationException>(() => { services.AddInfrastructure(config); });
        Assert.Contains(missing, ex.Message);
    }

    [Fact]
    public void Autentique_with_credentials_is_the_signature_provider()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=x;Username=x;Password=x",
            ["Signature:Provider"] = "Autentique",
            ["Autentique:ApiKey"] = "k",
            ["Autentique:WebhookSecret"] = "s",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(config);
        using var provider = services.BuildServiceProvider();

        var signature = provider.GetRequiredService<ISignatureProvider>();
        Assert.Equal(AutentiqueSignatureProvider.ProviderName, signature.Name);
    }
}

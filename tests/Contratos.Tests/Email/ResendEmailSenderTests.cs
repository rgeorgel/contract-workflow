using System.Net;
using System.Text.Json;
using Contratos.Application.Email;
using Contratos.Infrastructure;
using Contratos.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Contratos.Tests.Email;

public sealed class ResendEmailSenderTests
{
    private static readonly EmailMessage Message = new(
        "ana@cliente.com.br", "Ana", "Confirme seu e-mail", "<p>Olá</p>", "Olá");

    [Fact]
    public async Task Posts_the_message_to_the_emails_endpoint()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"id":"e-123"}"""));

        await Sender(handler).SendAsync(Message, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.resend.test/emails", request.Uri.ToString());
        Assert.Equal("Bearer re_test", request.Authorization);
        Assert.False(string.IsNullOrEmpty(request.IdempotencyKey));

        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("\"Contratos \\\"Teste\\\"\" <nao-responda@exemplo.com.br>", root.GetProperty("from").GetString());
        Assert.Equal(["ana@cliente.com.br"], root.GetProperty("to").EnumerateArray().Select(e => e.GetString()!));
        Assert.Equal("Confirme seu e-mail", root.GetProperty("subject").GetString());
        Assert.Equal("<p>Olá</p>", root.GetProperty("html").GetString());
        Assert.Equal("Olá", root.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Retries_rate_limits_and_server_errors_with_the_same_idempotency_key()
    {
        var responses = new Queue<HttpResponseMessage>([
            Json(HttpStatusCode.TooManyRequests, """{"name":"rate_limit_exceeded"}"""),
            Json(HttpStatusCode.BadGateway, "{}"),
            Json(HttpStatusCode.OK, """{"id":"e-1"}"""),
        ]);
        var handler = new StubHandler(_ => responses.Dequeue());

        await Sender(handler).SendAsync(Message, CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Single(handler.Requests.Select(r => r.IdempotencyKey).Distinct());
    }

    [Fact]
    public async Task Gives_up_after_the_last_attempt()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.ServiceUnavailable, "{}"));

        await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(handler).SendAsync(Message, CancellationToken.None));
        Assert.Equal(ResendEmailSender.MaxAttempts, handler.Requests.Count);
    }

    [Fact]
    public async Task Does_not_retry_a_rejected_message_and_keeps_the_recipient_out_of_the_error()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Forbidden,
            """{"statusCode":403,"name":"validation_error","message":"ana@cliente.com.br: domain is not verified"}"""));

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(handler).SendAsync(Message, CancellationToken.None));

        Assert.Single(handler.Requests);
        Assert.Contains("403 validation_error", ex.Message);
        Assert.DoesNotContain("ana@", ex.Message);
    }

    [Theory]
    [InlineData("", "a@b.com", "a@b.com")]
    [InlineData("Contratos", "a@b.com", "\"Contratos\" <a@b.com>")]
    [InlineData("Silva, Ana", "a@b.com", "\"Silva, Ana\" <a@b.com>")]
    public void Formats_the_sender(string name, string address, string expected) =>
        Assert.Equal(expected, ResendEmailSender.FormatMailbox(name, address));

    [Fact]
    public void Provider_resend_registers_the_resend_sender()
    {
        using var provider = Services(new()
        {
            ["Email:Provider"] = "Resend",
            ["Resend:ApiKey"] = "re_test",
        }).BuildServiceProvider();

        Assert.IsType<ResendEmailSender>(provider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void Provider_resend_without_api_key_fails_at_startup()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Services(new() { ["Email:Provider"] = "Resend" }));
        Assert.Contains("RESEND_API_KEY", ex.Message);
    }

    [Fact]
    public void Smtp_stays_the_default()
    {
        using var provider = Services(new()).BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<SmtpEmailSender>(scope.ServiceProvider.GetRequiredService<IEmailSender>());
    }

    private static ServiceCollection Services(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:Default"] = "Host=localhost;Database=x;Username=x;Password=x";
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(config);
        return services;
    }

    private static ResendEmailSender Sender(StubHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.test/") };
        http.DefaultRequestHeaders.Authorization = new("Bearer", "re_test");
        return new ResendEmailSender(
            http,
            Options.Create(new EmailOptions { FromAddress = "nao-responda@exemplo.com.br", FromName = "Contratos \"Teste\"" }),
            Options.Create(new ResendOptions { RetryDelay = TimeSpan.Zero }),
            NullLogger<ResendEmailSender>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? IdempotencyKey, string Body);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
                await request.Content!.ReadAsStringAsync(ct)));
            return respond(request);
        }
    }
}

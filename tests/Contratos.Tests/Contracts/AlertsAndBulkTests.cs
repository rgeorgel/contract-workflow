using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Contratos.Domain.Contracts;
using Contratos.Infrastructure.Persistence;
using Contratos.Tests.Infrastructure;
using Contratos.Tests.Templates;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contratos.Tests.Contracts;

[Collection(ApiCollection.Name)]
public sealed class AlertsAndBulkTests(ApiFactory factory)
{
    private static readonly Dictionary<string, string> ValidValues = new()
    {
        ["contratante.nome"] = "Café Aurora Ltda",
        ["contratante.cpf"] = "529.982.247-25",
        ["valor"] = "1500,50",
    };

    private static async Task<Guid> SentAsync(TestAccount admin, Guid templateId, string title, DateOnly? expiresAt = null)
    {
        var id = (await (await admin.Client.PostAsync("/api/v1/contracts", new { templateId, title })).JsonAsync()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PutAsync($"/api/v1/contracts/{id}",
            new { title, variableValues = ValidValues, signingMode = "Parallel", expiresAt })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/v1/contracts/{id}/signers",
            new { name = "Marina Costa", email = "marina@cliente.com.br", deliveryMethod = "Email" })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await admin.Client.PostAsync($"/api/v1/contracts/{id}/send")).StatusCode);
        return id;
    }

    private async Task BackdateSentAsync(Guid contractId, int days)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Contracts.IgnoreQueryFilters().Where(c => c.Id == contractId)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.SentAt, DateTimeOffset.UtcNow.AddDays(-days)));
    }

    private IReadOnlyList<Application.Email.EmailMessage> Attention(TestAccount who) =>
        factory.Emails.To(who.Email).Where(m => m.Subject.Contains("precisa", StringComparison.Ordinal)).ToList();

    // ---------- Alertas ----------

    [Fact]
    public async Task Creator_is_warned_once_when_a_contract_waits_too_long()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var templateId = (await TemplatesApiTests.CreatePublishedAsync(admin)).GetProperty("id").GetGuid();
        var waiting = await SentAsync(admin, templateId, "Consultoria parada");
        var recent = await SentAsync(admin, templateId, "Consultoria recente");
        await factory.ProcessBackgroundAsync(); // entrega ao provedor
        await BackdateSentAsync(waiting, 4);    // padrão: avisa depois de 3 dias

        await factory.ProcessBackgroundAsync();
        var mail = Assert.Single(Attention(admin));
        Assert.Contains("Consultoria parada", mail.TextBody);
        Assert.Contains("aguardando assinatura há 4 dias", mail.TextBody);
        Assert.Contains("Falta: Marina Costa", mail.TextBody);
        Assert.Contains($"https://app.test/contract.html?id={waiting}", mail.TextBody);
        Assert.DoesNotContain("Consultoria recente", mail.TextBody);

        await factory.ProcessBackgroundAsync();
        Assert.Single(Attention(admin)); // uma vez por envio
        _ = recent;
    }

    [Fact]
    public async Task Deadline_coming_warns_the_team_and_resends_the_link_to_who_has_not_signed()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var templateId = (await TemplatesApiTests.CreatePublishedAsync(admin)).GetProperty("id").GetGuid();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)).DateTime);
        var id = await SentAsync(admin, templateId, "Locação com prazo", today.AddDays(2));
        await factory.ProcessBackgroundAsync(); // entrega ao provedor
        await factory.ProcessBackgroundAsync(); // confere os alertas (só contratos que já estão no provedor)

        var mail = Assert.Single(Attention(admin));
        Assert.Contains("o prazo para assinar termina em 2 dias", mail.TextBody);

        var events = await (await admin.Client.GetAsync($"/api/v1/contracts/{id}/events")).JsonAsync();
        var resent = Assert.Single(events.EnumerateArray(), e => e.GetProperty("type").GetString() == "LinkResent");
        Assert.Equal("true", resent.GetProperty("payload").GetProperty("auto").GetString());
        Assert.Equal("Marina Costa", resent.GetProperty("signerName").GetString());
    }

    [Fact]
    public async Task Alerts_follow_the_organization_settings()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var settings = await (await admin.Client.GetAsync("/api/v1/organization/settings")).JsonAsync();
        Assert.Equal(3, settings.GetProperty("settings").GetProperty("staleAlertDays").GetInt32());
        Assert.True(settings.GetProperty("settings").GetProperty("autoResendBeforeExpiry").GetBoolean());

        var name = settings.GetProperty("name").GetString();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PutAsync("/api/v1/organization/settings",
            new { name, language = "pt-BR", staleAlertDays = 61 })).StatusCode);
        var saved = await (await admin.Client.PutAsync("/api/v1/organization/settings",
            new { name, language = "pt-BR", staleAlertDays = 0, expiryAlertDays = 0, autoResendBeforeExpiry = false })).JsonAsync();
        Assert.Equal(0, saved.GetProperty("settings").GetProperty("staleAlertDays").GetInt32());

        // Desligado: nem aviso nem reenvio.
        var templateId = (await TemplatesApiTests.CreatePublishedAsync(admin)).GetProperty("id").GetGuid();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)).DateTime);
        var id = await SentAsync(admin, templateId, "Sem alertas", today.AddDays(1));
        await factory.ProcessBackgroundAsync();
        await BackdateSentAsync(id, 10);
        await factory.ProcessBackgroundAsync();
        Assert.Empty(Attention(admin));
        var events = await (await admin.Client.GetAsync($"/api/v1/contracts/{id}/events")).JsonAsync();
        Assert.DoesNotContain(events.EnumerateArray(), e => e.GetProperty("type").GetString() == "LinkResent");

        // Mudar outros campos sem mandar os de alerta mantém o que estava.
        var kept = await (await admin.Client.PutAsync("/api/v1/organization/settings", new { name, language = "pt-BR" })).JsonAsync();
        Assert.False(kept.GetProperty("settings").GetProperty("autoResendBeforeExpiry").GetBoolean());
    }

    [Fact]
    public async Task Alerts_of_a_deactivated_creator_go_to_the_admins()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var editor = await TestAccounts.InviteAndAcceptAsync(factory, admin, "Editor");
        var templateId = (await TemplatesApiTests.CreatePublishedAsync(admin)).GetProperty("id").GetGuid();
        var id = await SentAsync(editor, templateId, "Do editor que saiu");
        await factory.ProcessBackgroundAsync();

        var users = await (await admin.Client.GetAsync("/api/v1/users")).JsonAsync();
        var editorId = users.EnumerateArray().Single(u => u.GetProperty("email").GetString() == editor.Email).GetProperty("id").GetGuid();
        await admin.Client.PutAsync($"/api/v1/users/{editorId}", new { active = false });
        await BackdateSentAsync(id, 5);
        await factory.ProcessBackgroundAsync();

        Assert.Empty(Attention(editor));
        Assert.Contains("Do editor que saiu", Assert.Single(Attention(admin)).TextBody);
    }

    // ---------- Lote ----------

    private static object Row(string title, Dictionary<string, string>? values = null, params object[] signers) =>
        new { title, variableValues = values ?? ValidValues, signers };

    private static object Signer(string name, string email) => new { name, email, deliveryMethod = "Email" };

    [Fact]
    public async Task Bulk_creates_one_draft_per_row_with_its_signers()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory); // Empresa: tem envio em lote
        var templateId = (await TemplatesApiTests.CreatePublishedAsync(admin)).GetProperty("id").GetGuid();
        object[] rows =
        [
            Row("Contrato Ana", null, Signer("Ana Lima", "ana@cliente.com.br"), Signer("Bruno Reis", "bruno@cliente.com.br")),
            Row("Contrato Caio", null, Signer("Caio Dias", "caio@cliente.com.br")),
            Row("Contrato Duda", null, new { name = "Duda Melo", phone = "11 98765-4321", deliveryMethod = "WhatsApp" }),
        ];

        var dry = await (await admin.Client.PostAsync("/api/v1/contracts/bulk", new { templateId, rows, dryRun = true })).JsonAsync();
        Assert.Equal(3, dry.GetProperty("valid").GetInt32());
        Assert.False(dry.GetProperty("created").GetBoolean());
        Assert.Equal(0, (await (await admin.Client.GetAsync("/api/v1/contracts")).JsonAsync()).GetProperty("total").GetInt32());

        var res = await admin.Client.PostAsync("/api/v1/contracts/bulk", new { templateId, rows, dryRun = false });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var created = await res.JsonAsync();
        Assert.True(created.GetProperty("created").GetBoolean());
        var numbers = created.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("number").GetInt32()).ToList();
        Assert.Equal([numbers[0], numbers[0] + 1, numbers[0] + 2], numbers);

        var first = await (await admin.Client.GetAsync($"/api/v1/contracts/{created.GetProperty("rows")[0].GetProperty("contractId").GetGuid()}")).JsonAsync();
        Assert.Equal("Draft", first.GetProperty("status").GetString());
        Assert.Equal("Contrato Ana", first.GetProperty("title").GetString());
        Assert.Equal(["Ana Lima", "Bruno Reis"], first.GetProperty("signers").EnumerateArray().Select(s => s.GetProperty("name").GetString()!).ToArray());
        Assert.Equal("Café Aurora Ltda", first.GetProperty("variableValues").GetProperty("contratante.nome").GetString());

        // Os rascunhos enviam normalmente (o envio usa a cota, um por um).
        var third = created.GetProperty("rows")[2].GetProperty("contractId").GetGuid();
        Assert.Equal(HttpStatusCode.Accepted, (await admin.Client.PostAsync($"/api/v1/contracts/{third}/send")).StatusCode);
    }

    [Fact]
    public async Task Bulk_with_any_invalid_row_creates_nothing_and_says_what_is_wrong()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var templateId = (await TemplatesApiTests.CreatePublishedAsync(admin)).GetProperty("id").GetGuid();
        var missingName = new Dictionary<string, string>(ValidValues);
        missingName.Remove("contratante.nome");
        object[] rows =
        [
            Row("Contrato certo", null, Signer("Ana Lima", "ana@cliente.com.br")),
            Row("Sem nome do contratante", missingName, Signer("Caio Dias", "caio@cliente.com.br")),
            Row("E-mail ruim e repetido", null, Signer("Duda Melo", "duda@"), Signer("Eva Luz", "eva@x.com.br"), Signer("Eva 2", "eva@x.com.br")),
            Row("Sem signatários", null),
        ];

        var res = await (await admin.Client.PostAsync("/api/v1/contracts/bulk", new { templateId, rows, dryRun = false })).JsonAsync();
        Assert.False(res.GetProperty("created").GetBoolean());
        Assert.Equal(1, res.GetProperty("valid").GetInt32());
        var errors = res.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("errors")).ToList();
        Assert.Empty(errors[0].EnumerateObject());
        Assert.True(errors[1].TryGetProperty("variableValues.contratante.nome", out _));
        Assert.True(errors[2].TryGetProperty("signers[0].email", out _));
        Assert.Contains("Já há um signatário", errors[2].GetProperty("signers[2].email")[0].GetString());
        Assert.True(errors[3].TryGetProperty("signers", out _));
        Assert.Equal(0, (await (await admin.Client.GetAsync("/api/v1/contracts")).JsonAsync()).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Bulk_is_part_of_the_professional_plans_and_has_a_row_limit()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var templateId = (await TemplatesApiTests.CreatePublishedAsync(admin)).GetProperty("id").GetGuid();
        var tooMany = Enumerable.Range(1, 101).Select(i => Row($"Contrato {i}", null, Signer("Ana", "ana@c.com.br"))).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PostAsync("/api/v1/contracts/bulk", new { templateId, rows = tooMany, dryRun = true })).StatusCode);

        var slug = (await (await admin.Client.GetAsync("/api/v1/organization/settings")).JsonAsync()).GetProperty("slug").GetString();
        using var platform = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        platform.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.PlatformKey);
        await platform.PutAsJsonAsync($"/api/platform/organizations/{slug}/plan", new { plan = "essencial" });

        var res = await admin.Client.PostAsync("/api/v1/contracts/bulk",
            new { templateId, rows = new[] { Row("Contrato", null, Signer("Ana", "ana@c.com.br")) }, dryRun = true });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        var problem = await res.JsonAsync();
        Assert.Equal("bulk_not_in_plan", problem.GetProperty("code").GetString());
        Assert.Contains("Profissional e Empresa", problem.GetProperty("title").GetString());

        var billing = await (await admin.Client.GetAsync("/api/v1/organization/billing")).JsonAsync();
        Assert.False(billing.GetProperty("plan").GetProperty("bulkSend").GetBoolean());
    }
}

using System.Net;
using System.Text.Json;
using Contratos.Application.Templates;
using Contratos.Domain.Templates;
using Contratos.Infrastructure.Security;
using Contratos.Tests.Infrastructure;

namespace Contratos.Tests.Templates;

[Collection(ApiCollection.Name)]
public sealed class TemplateStartersTests(ApiFactory factory)
{
    public static TheoryData<string> StarterIds() => [.. TemplateStarters.All.Select(s => s.Id)];

    [Theory]
    [MemberData(nameof(StarterIds))]
    public void Each_starter_is_complete_and_survives_the_sanitizer(string id)
    {
        var starter = TemplateStarters.Find(id)!;

        var save = TemplateValidator.ValidateSave(new SaveTemplateRequest(
            starter.Name, starter.Description, starter.BodyHtml, [.. starter.Variables], [.. starter.DefaultSignerRoles]));
        Assert.True(save.IsEmpty, string.Join("; ", save.ToDictionary().SelectMany(e => e.Value)));
        Assert.True(TemplateValidator.ValidatePublish(starter.BodyHtml, starter.Variables).IsEmpty);

        // Toda variável cadastrada aparece no texto, e o texto não perde nada ao ser sanitizado.
        Assert.Equal(starter.Variables.Select(v => v.Key).Order(), Placeholders.Extract(starter.BodyHtml).Order());
        var clean = new TemplateHtmlSanitizer().Sanitize(starter.BodyHtml);
        Assert.Equal(Placeholders.Extract(starter.BodyHtml), Placeholders.Extract(clean));
        Assert.Contains("<h1>", clean);
    }

    [Fact]
    public async Task Admin_lists_the_three_starters()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);

        var list = await (await admin.Client.GetAsync("/api/v1/templates/starters")).JsonAsync();

        Assert.Equal(3, list.GetArrayLength());
        Assert.All(list.EnumerateArray(), s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.GetProperty("name").GetString()));
            Assert.True(s.GetProperty("variableCount").GetInt32() > 0);
        });
    }

    [Fact]
    public async Task Creating_from_a_starter_fills_the_draft_and_can_be_published_right_away()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);

        var res = await admin.Client.PostAsync("/api/v1/templates", new { name = "Meu NDA", starterId = "confidencialidade" });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = await res.JsonAsync();
        Assert.Equal("Meu NDA", created.GetProperty("name").GetString());
        var draft = created.GetProperty("draft");
        Assert.Contains("Acordo de Confidencialidade", draft.GetProperty("bodyHtml").GetString());
        Assert.Equal(TemplateStarters.Find("confidencialidade")!.Variables.Count, draft.GetProperty("variables").GetArrayLength());
        Assert.Equal(2, draft.GetProperty("defaultSignerRoles").GetArrayLength());

        var publish = await admin.Client.PostAsync($"/api/v1/templates/{created.GetProperty("id").GetGuid()}/publish");
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
    }

    [Fact]
    public async Task Blank_is_still_the_default()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);

        var created = await (await admin.Client.PostAsync("/api/v1/templates", new { name = "Do zero" })).JsonAsync();

        Assert.Equal("", created.GetProperty("draft").GetProperty("bodyHtml").GetString());
        Assert.Equal(0, created.GetProperty("draft").GetProperty("variables").GetArrayLength());
    }

    [Fact]
    public async Task Unknown_starter_is_a_validation_error()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);

        var res = await admin.Client.PostAsync("/api/v1/templates", new { name = "X", starterId = "nao-existe" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await res.JsonAsync()).GetProperty("errors").TryGetProperty("starterId", out _));
    }

    [Fact]
    public async Task Unsaved_preview_renders_the_editor_content_without_saving()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);

        var res = await admin.Client.PostAsync("/api/v1/templates/preview", new
        {
            name = "Rascunho",
            bodyHtml = "<h1>Teste</h1><p>{{cliente.nome}} paga {{valor}} até {{prazo}}.</p><script>alert(1)</script><img src=\"https://evil.example/x.png\">",
            variables = new object[]
            {
                new { key = "cliente.nome", label = "Nome do cliente", type = "Text", required = true },
                new { key = "valor", label = "Valor", type = "Money", required = true },
                // Linhas que o editor ainda está preenchendo: chave repetida, vazia ou inválida.
                new { key = "valor", label = "Outro valor", type = "Text", required = true },
                new { key = "", label = "", type = "Text", required = false },
                new { key = "1errado", label = "x", type = "Text", required = false },
            },
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("default-src 'none'", string.Join(';', res.Headers.GetValues("Content-Security-Policy")));
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("<h1>Teste</h1>", html);
        Assert.Contains("R$", html);
        Assert.Contains("[prazo]", html); // não cadastrada: aparece destacada
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("evil.example", html);

        // Nada foi gravado.
        var list = await (await admin.Client.GetAsync("/api/v1/templates")).JsonAsync();
        Assert.Equal(0, list.GetArrayLength());
    }

    [Fact]
    public async Task Only_admins_use_starters_and_unsaved_preview()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var editor = await TestAccounts.InviteAndAcceptAsync(factory, admin, "Editor");

        Assert.Equal(HttpStatusCode.Forbidden, (await editor.Client.GetAsync("/api/v1/templates/starters")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await editor.Client.PostAsync("/api/v1/templates/preview", new { bodyHtml = "<p>x</p>" })).StatusCode);
    }
}

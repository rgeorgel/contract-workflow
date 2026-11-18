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

    [Theory]
    [MemberData(nameof(StarterIds))]
    public void Each_starter_reads_well_with_and_without_the_optional_variables(string id)
    {
        var starter = TemplateStarters.Find(id)!;
        var renderer = new DocumentRenderer(new TemplateHtmlSanitizer());
        var required = starter.Variables.Where(v => v.Required)
            .ToDictionary(v => v.Key, VariableValues.Sample, StringComparer.Ordinal);

        // Variável opcional fora de um {{#se}} apareceria como [rótulo] pendente no contrato.
        var minimal = renderer.Render(starter.Name, starter.BodyHtml, starter.Variables, required);
        Assert.DoesNotContain("class=\"missing\"", minimal);
        Assert.DoesNotContain("{{", minimal);

        var full = renderer.RenderSample(starter.Name, starter.BodyHtml, starter.Variables);
        Assert.DoesNotContain("class=\"missing\"", full);
    }

    [Fact]
    public void Every_sector_has_at_least_three_starters_and_ids_are_unique()
    {
        Assert.Equal(TemplateStarters.All.Count, TemplateStarters.All.Select(s => s.Id).Distinct().Count());
        var categories = TemplateStarters.Categories.Select(c => c.Id).ToHashSet();
        Assert.All(TemplateStarters.All, s =>
        {
            Assert.Contains(s.Category, categories);
            Assert.NotEmpty(s.DefaultSignerRoles);
        });
        Assert.All(TemplateStarters.Categories, c =>
            Assert.True(TemplateStarters.All.Count(s => s.Category == c.Id) >= 3, $"O setor {c.Id} tem menos de 3 modelos."));
    }

    [Fact]
    public async Task Admin_lists_the_starters_grouped_by_sector()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);

        var catalog = await (await admin.Client.GetAsync("/api/v1/templates/starters")).JsonAsync();

        var categories = catalog.GetProperty("categories").EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToList();
        Assert.Contains("advocacia", categories);
        Assert.Contains("contabilidade", categories);
        Assert.Contains("imobiliario", categories);
        Assert.Contains("rh", categories);
        var list = catalog.GetProperty("starters");
        Assert.Equal(TemplateStarters.All.Count, list.GetArrayLength());
        Assert.Contains(list.EnumerateArray(), s => s.GetProperty("id").GetString() == "honorarios-advocaticios");
        Assert.All(list.EnumerateArray(), s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.GetProperty("name").GetString()));
            Assert.Contains(s.GetProperty("category").GetString(), categories);
            Assert.True(s.GetProperty("variableCount").GetInt32() > 0);
            Assert.True(s.GetProperty("signers").GetArrayLength() > 0);
        });
        var lease = list.EnumerateArray().Single(s => s.GetProperty("id").GetString() == "locacao-residencial");
        Assert.Equal(["Locador", "Locatário"], lease.GetProperty("signers").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Starter_detail_shows_the_text_with_every_variable_highlighted()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);

        var res = await admin.Client.GetAsync("/api/v1/templates/starters/honorarios-advocaticios");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var detail = await res.JsonAsync();
        Assert.Equal("advocacia", detail.GetProperty("category").GetString());
        Assert.Equal(TemplateStarters.Find("honorarios-advocaticios")!.Variables.Count, detail.GetProperty("variables").GetArrayLength());
        Assert.Equal(4, detail.GetProperty("defaultSignerRoles").GetArrayLength());
        var html = detail.GetProperty("previewHtml").GetString()!;
        Assert.Contains("<h1>Contrato de Prestação de Serviços Advocatícios</h1>", html);
        Assert.Contains("<span class=\"var\">Nome do contratante</span>", html);
        Assert.Contains("Processo relacionado", html); // trechos opcionais aparecem na pré-visualização
        Assert.DoesNotContain("{{", html);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.Client.GetAsync("/api/v1/templates/starters/nao-existe")).StatusCode);
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
    public void Honorarios_starter_groups_keys_by_party_and_suggests_optional_witnesses()
    {
        var starter = TemplateStarters.Find("honorarios-advocaticios")!;
        var byKey = starter.Variables.ToDictionary(v => v.Key);

        // O Causify reconhece a parte pela chave e pelo rótulo, e a fonte pelo tipo.
        Assert.All(starter.Variables.Where(v => v.Key.StartsWith("contratante.", StringComparison.Ordinal)),
            v => Assert.Contains("contratante", v.Label, StringComparison.Ordinal));
        Assert.Equal(VariableType.CpfCnpj, byKey["contratante.documento"].Type);
        Assert.Equal(VariableType.CpfCnpj, byKey["contratada.cnpj"].Type);
        Assert.Equal(VariableType.Money, byKey["valor.total"].Type);
        Assert.Equal(VariableType.Money, byKey["valor.entrada"].Type);
        Assert.Equal(VariableType.Date, byKey["contrato.inicio"].Type);
        Assert.Equal(VariableType.Number, byKey["multa.percentual"].Type);
        Assert.DoesNotContain(starter.Variables, v => v.Label.Contains("comarca", StringComparison.OrdinalIgnoreCase));

        string[] optional = ["contratante.qualificacao", "contratante.email", "contratada.cnpj", "processo.numero",
            "valor.entrada", "parcelas.quantidade", "pagamento.primeiro_vencimento", "exito.percentual"];
        Assert.Equal(optional.Order(), starter.Variables.Where(v => !v.Required).Select(v => v.Key).Order());

        Assert.Equal(
            [("Contratante", SignerAction.Sign), ("Contratada", SignerAction.Sign),
             ("Testemunha", SignerAction.SignAsWitness), ("Testemunha", SignerAction.SignAsWitness)],
            starter.DefaultSignerRoles.OrderBy(r => r.Order).Select(r => (r.Role, r.Action)));
    }

    [Fact]
    public void Honorarios_starter_reads_well_without_the_optional_variables()
    {
        var starter = TemplateStarters.Find("honorarios-advocaticios")!;
        var renderer = new DocumentRenderer(new TemplateHtmlSanitizer());
        var required = starter.Variables.Where(v => v.Required)
            .ToDictionary(v => v.Key, VariableValues.Sample, StringComparer.Ordinal);

        var html = renderer.Render(starter.Name, starter.BodyHtml, starter.Variables, required);

        Assert.DoesNotContain("class=\"missing\"", html);
        Assert.DoesNotContain("{{", html);
        Assert.DoesNotContain("Processo relacionado", html);
        Assert.DoesNotContain("êxito", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Entrada de", html);
        Assert.Contains("Forma de pagamento: Forma de pagamento.</p>", html);
        Assert.Contains("com sede em", html);

        var all = renderer.RenderSample(starter.Name, starter.BodyHtml, starter.Variables);
        Assert.Contains("1.3. Processo relacionado", all);
        Assert.Contains("2.4. Honorários de êxito: 10%", all);
        Assert.Contains("Pagamento em 10 parcela(s), com o primeiro vencimento em", all);
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
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.Client.GetAsync("/api/v1/templates/starters/confidencialidade")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await editor.Client.PostAsync("/api/v1/templates/preview", new { bodyHtml = "<p>x</p>" })).StatusCode);
    }
}

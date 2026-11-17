using Contratos.Application.Templates;
using Contratos.Domain.Templates;
using Contratos.Infrastructure.Security;

namespace Contratos.Tests.Domain;

public sealed class TemplateRulesTests
{
    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224724", false)]
    [InlineData("111.111.111-11", false)]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("11.222.333/0001-80", false)]
    public void Validates_cpf_and_cnpj(string value, bool valid) =>
        Assert.Equal(valid, Documents.IsValidCpfOrCnpj(value));

    [Theory]
    [InlineData(VariableType.Money, "1500.5", "R$ 1.500,50")]
    [InlineData(VariableType.Money, "1.500,50", "R$ 1.500,50")]
    [InlineData(VariableType.Date, "2026-11-13", "13/11/2026")]
    [InlineData(VariableType.CpfCnpj, "11222333000181", "11.222.333/0001-81")]
    [InlineData(VariableType.Number, "1234.5", "1.234,5")]
    public void Formats_values_the_brazilian_way(VariableType type, string raw, string expected) =>
        Assert.Equal(expected, VariableValues.Format(type, raw));

    [Fact]
    public void Extracts_placeholders_with_spaces_and_dots()
    {
        var keys = Placeholders.Extract("<p>{{ a.b }} {{c}} {{a.b}} {{ 1x }}</p>");
        Assert.Equal(["a.b", "c"], keys);
    }

    [Fact]
    public void Renderer_encodes_values_so_they_cannot_inject_html()
    {
        var renderer = new DocumentRenderer(new TemplateHtmlSanitizer());
        var html = renderer.Render("T", "<p>{{nome}}</p>",
            [new TemplateVariable("nome", "Nome", VariableType.Text, true)],
            new Dictionary<string, string> { ["nome"] = "<img src=x onerror=alert(1)>" });

        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;img", html);
    }

    [Fact]
    public void Missing_values_show_the_label_highlighted()
    {
        var renderer = new DocumentRenderer(new TemplateHtmlSanitizer());
        var html = renderer.Render("T", "<p>{{nome}}</p>", [new TemplateVariable("nome", "Nome completo", VariableType.Text, true)],
            new Dictionary<string, string>());

        Assert.Contains("[Nome completo]", html);
    }

    private static readonly Func<string, bool> OnlyA = key => key == "a";

    [Theory]
    [InlineData("<p>x{{#se a}} y{{/se}}.</p>", "<p>x y.</p>")]
    [InlineData("<p>x{{#se b}} y{{/se}}.</p>", "<p>x.</p>")]
    [InlineData("<p>1</p>{{#se b}}<p>2</p>{{/se}}<p>3</p>", "<p>1</p><p>3</p>")]
    // Marca sozinha no parágrafo (como o editor visual grava): o parágrafo vai junto.
    [InlineData("<p>1</p><p>{{#se b}}</p><p>2</p><p>{{/se}}</p><p>3</p>", "<p>1</p><p>3</p>")]
    [InlineData("<p>1</p><p>{{#se a}}</p><p>2</p><p> {{/se}}<br></p>", "<p>1</p><p>2</p>")]
    // Marca no começo de um parágrafo com texto: o <p> fica.
    [InlineData("<p>{{#se a}}x{{/se}} y</p>", "<p>x y</p>")]
    [InlineData("<p>{{ #se  a }}x{{ /se }}</p>", "<p>x</p>")]
    // Trecho dentro de trecho.
    [InlineData("{{#se a}}A{{#se b}}B{{/se}}C{{/se}}", "AC")]
    [InlineData("{{#se b}}A{{#se a}}B{{/se}}C{{/se}}D", "D")]
    public void Conditional_blocks_show_only_when_the_variable_is_filled(string html, string expected) =>
        Assert.Equal(expected, Placeholders.ApplyConditionals(html, OnlyA));

    [Fact]
    public void Conditional_keys_count_as_used_variables()
    {
        Assert.Equal(["x", "a", "y"], Placeholders.Extract("<p>{{x}} {{#se a}}texto{{/se}} {{#se y}}{{y}}{{/se}}</p>"));

        var errors = TemplateValidator.ValidatePublish("<p>{{#se a}}texto{{/se}}</p>", []).ToDictionary();
        Assert.Contains("a", Assert.Single(errors["bodyHtml"]));
    }

    [Theory]
    [InlineData("<p>{{#se a}}texto</p>", "{{#se a}}")]
    [InlineData("<p>texto{{/se}}</p>", "{{/se}} sem")]
    [InlineData("<p>{{/se}}{{#se a}}</p>", "{{/se}} sem")]
    public void Publishing_refuses_unbalanced_conditionals(string body, string message)
    {
        var errors = TemplateValidator.ValidatePublish(body, [new TemplateVariable("a", "A", VariableType.Text, false)]).ToDictionary();
        Assert.Contains(message, Assert.Single(errors["bodyHtml"]));
    }

    [Fact]
    public void Renderer_drops_conditional_text_of_empty_optional_variables()
    {
        var renderer = new DocumentRenderer(new TemplateHtmlSanitizer());
        TemplateVariable[] vars =
        [
            new("nome", "Nome", VariableType.Text, true),
            new("processo", "Número do processo", VariableType.Text, false),
            new("exito", "Êxito (%)", VariableType.Number, false),
        ];
        const string body = "<p>{{nome}}{{#se processo}}, processo {{processo}}{{/se}}.</p>{{#se exito}}<p>Êxito de {{exito}}%.</p>{{/se}}";

        var empty = renderer.Render("T", body, vars, new Dictionary<string, string> { ["nome"] = "Ana", ["processo"] = "  " });
        Assert.Contains("<p>Ana.</p>", empty);
        Assert.DoesNotContain("processo", empty);
        Assert.DoesNotContain("Êxito", empty);
        Assert.DoesNotContain("{{", empty);

        var full = renderer.Render("T", body, vars,
            new Dictionary<string, string> { ["nome"] = "Ana", ["processo"] = "0001", ["exito"] = "1,5" });
        Assert.Contains("<p>Ana, processo 0001.</p>", full);
        Assert.Contains("<p>Êxito de 1,5%.</p>", full);
    }
}

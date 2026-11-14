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
}

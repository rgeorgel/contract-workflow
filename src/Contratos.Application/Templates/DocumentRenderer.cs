using System.Net;
using Contratos.Application.Common;
using Contratos.Domain.Templates;

namespace Contratos.Application.Templates;

/// <summary>Monta o HTML final do contrato: corpo do template + valores, dentro de uma página A4.</summary>
public sealed class DocumentRenderer(IHtmlSanitizer sanitizer)
{
    /// <param name="values">Valores por chave; o que faltar aparece destacado como <c>[rótulo]</c>.</param>
    public string Render(string title, string bodyHtml, IReadOnlyList<TemplateVariable> variables,
        IReadOnlyDictionary<string, string> values)
    {
        var byKey = variables.ToDictionary(v => v.Key, StringComparer.Ordinal);
        var filled = Placeholders.Replace(bodyHtml, key =>
        {
            byKey.TryGetValue(key, out var variable);
            if (values.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                var formatted = variable is null ? raw : VariableValues.Format(variable.Type, raw);
                return WebUtility.HtmlEncode(formatted).Replace("\n", "<br>", StringComparison.Ordinal);
            }
            var label = variable?.Label ?? key;
            return $"<span class=\"missing\">[{WebUtility.HtmlEncode(label)}]</span>";
        });

        return Page(title, sanitizer.Sanitize(filled));
    }

    /// <summary>Pré-visualização com valores de exemplo.</summary>
    public string RenderSample(string title, string bodyHtml, IReadOnlyList<TemplateVariable> variables) =>
        Render(title, bodyHtml, variables, variables.ToDictionary(v => v.Key, VariableValues.Sample, StringComparer.Ordinal));

    private static string Page(string title, string body) => $$"""
        <!doctype html>
        <html lang="pt-BR">
        <head>
        <meta charset="utf-8">
        <title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>
          @page { size: A4; margin: 25mm 20mm; }
          html { background: #fff; }
          body { margin: 0; font-family: "Helvetica Neue", Arial, sans-serif; font-size: 11pt; line-height: 1.55; color: #111; }
          h1, h2, h3 { line-height: 1.25; }
          h1 { font-size: 16pt; text-align: center; margin: 0 0 18pt; }
          h2 { font-size: 12.5pt; margin: 16pt 0 6pt; }
          p { margin: 0 0 8pt; text-align: justify; }
          table { border-collapse: collapse; width: 100%; }
          td, th { border: 1px solid #999; padding: 4pt 6pt; vertical-align: top; }
          img { max-width: 100%; }
          .missing { background: #fff3cd; color: #8f5300; padding: 0 2pt; border-radius: 2pt; }
          @media screen { body { padding: 32px 40px; max-width: 760px; margin: 0 auto; } }
        </style>
        </head>
        <body>
        {{body}}
        </body>
        </html>
        """;
}

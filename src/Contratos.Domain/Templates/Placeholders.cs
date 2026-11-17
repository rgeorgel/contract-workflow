using System.Text;
using System.Text.RegularExpressions;

namespace Contratos.Domain.Templates;

public static partial class Placeholders
{
    /// <summary><c>{{ contratante.nome }}</c> → "contratante.nome".</summary>
    [GeneratedRegex(@"\{\{\s*([a-zA-Z][a-zA-Z0-9_]*(?:\.[a-zA-Z][a-zA-Z0-9_]*)*)\s*\}\}")]
    public static partial Regex Pattern();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)*$")]
    public static partial Regex KeyPattern();

    /// <summary>
    /// Abertura (<c>{{#se chave}}</c>) ou fechamento (<c>{{/se}}</c>) de um trecho condicional.
    /// Quando a marca está sozinha num parágrafo (o editor visual faz isso), o parágrafo vai junto,
    /// para não sobrar um &lt;p&gt; vazio no documento.
    /// </summary>
    [GeneratedRegex(@"<p\b[^>]*>\s*(?:<br\s*/?>\s*)*" + Tag + @"(?:\s*<br\s*/?>)*\s*</p>|" + Tag, RegexOptions.IgnoreCase)]
    private static partial Regex ConditionalTag();

    private const string Tag = @"\{\{\s*(?:(?<open>#se)\s+(?<key>[a-zA-Z][a-zA-Z0-9_]*(?:\.[a-zA-Z][a-zA-Z0-9_]*)*)|(?<close>/se))\s*\}\}";

    /// <summary>Chaves usadas no texto, nas variáveis e nos <c>{{#se chave}}</c>, na ordem em que aparecem.</summary>
    public static IReadOnlyList<string> Extract(string html)
    {
        var keys = Pattern().Matches(html).Select(m => (m.Index, Key: m.Groups[1].Value))
            .Concat(ConditionalTag().Matches(html).Where(m => m.Groups["open"].Success).Select(m => (m.Index, Key: m.Groups["key"].Value)))
            .OrderBy(k => k.Index);
        return keys.Select(k => k.Key).Distinct(StringComparer.Ordinal).ToList();
    }

    public static string Replace(string html, Func<string, string> valueFor) =>
        Pattern().Replace(html, m => valueFor(m.Groups[1].Value));

    /// <summary>
    /// Resolve os trechos <c>{{#se chave}} … {{/se}}</c>: o conteúdo fica quando <paramref name="isFilled"/>
    /// diz que a variável tem valor, e some (com as marcas) quando não tem. Aceita trechos dentro de trechos.
    /// Marcas sem par são descartadas; a publicação já recusa esse caso (<see cref="CheckConditionals"/>).
    /// </summary>
    public static string ApplyConditionals(string html, Func<string, bool> isFilled)
    {
        var output = new StringBuilder(html.Length);
        var visible = new Stack<bool>();
        var showing = true;
        var last = 0;
        foreach (Match m in ConditionalTag().Matches(html))
        {
            if (showing)
                output.Append(html, last, m.Index - last);
            last = m.Index + m.Length;

            if (m.Groups["open"].Success)
            {
                visible.Push(showing);
                showing = showing && isFilled(m.Groups["key"].Value);
            }
            else if (visible.Count > 0)
                showing = visible.Pop();
        }
        if (showing)
            output.Append(html, last, html.Length - last);
        return output.ToString();
    }

    /// <summary>Mensagem de erro se algum <c>{{#se}}</c> ficou sem <c>{{/se}}</c> (ou o contrário); null se está tudo fechado.</summary>
    public static string? CheckConditionals(string html)
    {
        var open = new Stack<string>();
        foreach (Match m in ConditionalTag().Matches(html))
        {
            if (m.Groups["open"].Success)
                open.Push(m.Groups["key"].Value);
            else if (open.Count == 0)
                return "Há um {{/se}} sem o {{#se …}} correspondente.";
            else
                open.Pop();
        }
        return open.Count == 0 ? null : $"Feche o trecho {{{{#se {open.Peek()}}}}} com {{{{/se}}}}.";
    }
}

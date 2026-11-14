using System.Text.RegularExpressions;

namespace Contratos.Domain.Templates;

public static partial class Placeholders
{
    /// <summary><c>{{ contratante.nome }}</c> → "contratante.nome".</summary>
    [GeneratedRegex(@"\{\{\s*([a-zA-Z][a-zA-Z0-9_]*(?:\.[a-zA-Z][a-zA-Z0-9_]*)*)\s*\}\}")]
    public static partial Regex Pattern();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)*$")]
    public static partial Regex KeyPattern();

    public static IReadOnlyList<string> Extract(string html) =>
        Pattern().Matches(html).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    public static string Replace(string html, Func<string, string> valueFor) =>
        Pattern().Replace(html, m => valueFor(m.Groups[1].Value));
}

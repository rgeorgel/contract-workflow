using System.Globalization;
using System.Text;

namespace Contratos.Domain.Organizations;

public static class Slug
{
    /// <summary>"Ação & Cia. Ltda" → "acao-cia-ltda".</summary>
    public static string From(string text, int maxLength = Organization.SlugMaxLength)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var lastWasDash = true;

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                sb.Append('-');
                lastWasDash = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > maxLength)
            slug = slug[..maxLength].TrimEnd('-');

        return slug.Length == 0 ? "org" : slug;
    }
}

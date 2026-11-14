using System.Text.RegularExpressions;
using Ganss.Xss;

namespace Contratos.Infrastructure.Security;

/// <summary>
/// Sanitização dos templates. Além de tirar scripts e eventos, bloqueia URLs externas em imagens e CSS:
/// o HTML vira PDF no Gotenberg, dentro da rede interna, e uma URL ali seria uma porta para SSRF.
/// Imagens só embutidas (data:image/...); links só https e mailto.
/// </summary>
internal sealed partial class TemplateHtmlSanitizer : Application.Common.IHtmlSanitizer
{
    private readonly HtmlSanitizer _sanitizer;

    public TemplateHtmlSanitizer()
    {
        _sanitizer = new HtmlSanitizer();
        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.UnionWith(["https", "mailto", "data"]);
        _sanitizer.AllowedAttributes.Add("class");
        _sanitizer.AllowDataAttributes = false;
        _sanitizer.FilterUrl += (_, e) =>
        {
            var url = e.OriginalUrl.Trim();
            var isLink = string.Equals(e.Tag?.TagName, "A", StringComparison.OrdinalIgnoreCase);
            var allowed = EmbeddedImage().IsMatch(url)
                || (isLink && (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                               || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)));
            if (!allowed)
                e.SanitizedUrl = null;
        };
    }

    public string Sanitize(string html) => _sanitizer.Sanitize(html);

    [GeneratedRegex(@"^data:image/(png|jpeg|gif|webp);base64,[A-Za-z0-9+/=\s]+$", RegexOptions.IgnoreCase)]
    private static partial Regex EmbeddedImage();
}

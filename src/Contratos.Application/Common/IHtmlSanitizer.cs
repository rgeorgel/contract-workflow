namespace Contratos.Application.Common;

/// <summary>Limpa HTML de templates: sem scripts, eventos, iframes nem URLs externas.</summary>
public interface IHtmlSanitizer
{
    string Sanitize(string html);
}

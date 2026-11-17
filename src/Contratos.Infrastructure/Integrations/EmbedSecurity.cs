using System.Security.Cryptography;
using System.Text;

namespace Contratos.Infrastructure.Integrations;

/// <summary>Tokens do embed: <c>cel_…</c> (uso único, vai no link) e <c>ces_…</c> (acesso, fica na página).</summary>
public static class EmbedTokens
{
    public const string LaunchPrefix = "cel_";
    public const string AccessPrefix = "ces_";

    public static (string Token, byte[] Hash) Create(string prefix)
    {
        var token = prefix + ApiKeyTokens.Base64Url(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public static bool LooksValid(string token, string prefix) =>
        token.StartsWith(prefix, StringComparison.Ordinal) && token.Length is > 20 and < 100;
}

/// <summary>
/// Domínios que podem abrir o embed. Entram no cabeçalho <c>Content-Security-Policy: frame-ancestors</c>,
/// então só passa o formato de origem (esquema, host e porta), sem caminho nem nada que quebre o cabeçalho.
/// </summary>
public static class EmbedOrigins
{
    public const int MaxPerOrganization = 10;

    /// <summary>Devolve a origem normalizada ou a mensagem de erro.</summary>
    public static (string? Origin, string? Error) Normalize(string? value, bool allowWildcard = true)
    {
        var raw = value?.Trim().TrimEnd('/') ?? "";
        if (raw.Length == 0)
            return (null, "Informe o endereço do site, ex.: https://crm.suaempresa.com.br.");
        if (raw.Length > EmbedOrigin.MaxLength)
            return (null, $"Use no máximo {EmbedOrigin.MaxLength} caracteres.");

        var wildcard = false;
        var probe = raw;
        var schemeEnd = raw.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd > 0 && raw.AsSpan(schemeEnd + 3).StartsWith("*."))
        {
            if (!allowWildcard)
                return (null, "Informe o endereço exato do site, sem *.");
            wildcard = true;
            probe = raw[..(schemeEnd + 3)] + "wildcard" + raw[(schemeEnd + 4)..];
        }

        if (!Uri.TryCreate(probe, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return (null, "Informe o endereço completo, começando com https://.");
        if (!string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0
            || probe.Contains('?') || probe.Contains('#'))
            return (null, "Informe só o endereço do site (ex.: https://crm.suaempresa.com.br), sem caminho nem parâmetros.");

        var host = uri.IdnHost.ToLowerInvariant();
        var local = host is "localhost" or "127.0.0.1" || host.EndsWith(".localhost", StringComparison.Ordinal);
        if (uri.Scheme == Uri.UriSchemeHttp && !local)
            return (null, "Use https://. Endereço http:// só vale para localhost, durante o desenvolvimento.");
        if (wildcard)
        {
            // *.com ou *.com.br liberaria qualquer site: exige ao menos dois níveis depois do *.
            var rest = host["wildcard.".Length..];
            if (rest.Count(c => c == '.') < 1 || rest.Split('.').Any(p => p.Length == 0))
                return (null, "Use o * só no começo de um domínio seu, ex.: https://*.suaempresa.com.br.");
            host = "*." + rest;
        }
        else if (uri.HostNameType == UriHostNameType.Dns && !local && !host.Contains('.'))
            return (null, "Informe um domínio completo, ex.: https://crm.suaempresa.com.br.");

        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        return ($"{uri.Scheme}://{host}{port}", null);
    }

    /// <summary>A origem (exata, vinda do navegador) é permitida por um dos padrões cadastrados.</summary>
    public static bool Matches(string pattern, string origin)
    {
        if (string.Equals(pattern, origin, StringComparison.OrdinalIgnoreCase))
            return true;
        var star = pattern.IndexOf("://*.", StringComparison.Ordinal);
        if (star < 0)
            return false;
        var scheme = pattern[..(star + 3)];
        var suffix = pattern[(star + 4)..]; // ".suaempresa.com.br[:porta]"
        return origin.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
            && origin.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && origin.Length > scheme.Length + suffix.Length
            && !origin[scheme.Length..^suffix.Length].Contains(':');
    }

    /// <summary>Valor do <c>frame-ancestors</c>; sem origens, ninguém pode emoldurar.</summary>
    public static string FrameAncestors(IEnumerable<string> origins)
    {
        var list = origins.ToList();
        return list.Count == 0 ? "'none'" : string.Join(' ', list);
    }
}

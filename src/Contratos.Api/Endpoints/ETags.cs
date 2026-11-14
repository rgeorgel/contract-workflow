namespace Contratos.Api.Endpoints;

/// <summary>Concorrência otimista: o GET devolve ETag com a <c>xmin</c>; o PUT pode mandar If-Match.</summary>
internal static class ETags
{
    public static string Format(uint rowVersion) => $"\"{rowVersion}\"";

    public static void Set(HttpContext ctx, uint rowVersion) => ctx.Response.Headers.ETag = Format(rowVersion);

    /// <summary>null se o cabeçalho não veio; lança se veio malformado.</summary>
    public static uint? IfMatch(HttpContext ctx)
    {
        var raw = ctx.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(raw) || raw == "*")
            return null;
        var value = raw.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal))
            value = value[2..];
        return uint.TryParse(value.Trim('"'), out var v) ? v : throw new BadHttpRequestException("If-Match inválido.");
    }
}

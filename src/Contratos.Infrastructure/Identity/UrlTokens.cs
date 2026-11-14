using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Contratos.Infrastructure.Identity;

/// <summary>Tokens do Identity em base64url, para irem na query string dos links.</summary>
internal static class UrlTokens
{
    public static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    public static string? Decode(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Contratos.Infrastructure.Integrations;

public sealed class WebhookOptions
{
    public const string Section = "Webhooks";
    /// <summary>
    /// Aceita http:// e endereços de rede interna (localhost, 10.x, 192.168.x...). Só para desenvolvimento:
    /// em produção um cliente poderia apontar o webhook para dentro da nossa rede.
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
    /// <summary>Espera entre tentativas; vazio usa 1 min, 5 min, 30 min, 2 h, 6 h, 12 h e 24 h (8 tentativas, ~45 h).</summary>
    public int[]? RetryDelaysSeconds { get; set; }
    public int Concurrency { get; set; } = 8;

    public int[] Delays => RetryDelaysSeconds is { Length: > 0 } d ? d : [60, 300, 1800, 7200, 21600, 43200, 86400];
}

/// <summary>Chaves de API: <c>ctr_</c> + 32 bytes aleatórios. Só o SHA-256 vai para o banco.</summary>
public static class ApiKeyTokens
{
    public const string Prefix = "ctr_";
    private const int DisplayLength = 12;

    public static (string Token, string Display, byte[] Hash) Create()
    {
        var token = Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        return (token, token[..DisplayLength], Hash(token));
    }

    /// <summary>Chave de alta entropia: um hash rápido basta (não é senha escolhida por gente).</summary>
    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public static bool LooksValid(string token) =>
        token.StartsWith(Prefix, StringComparison.Ordinal) && token.Length is > 20 and < 100;

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Segredo de assinatura dos webhooks, cifrado com o Data Protection (as chaves ficam no volume da app).</summary>
public sealed class WebhookSecrets(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Contratos.Webhooks.Secret.v1");

    public static string NewSecret() => "whsec_" + ApiKeyTokens.Base64Url(RandomNumberGenerator.GetBytes(32));

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}

/// <summary>
/// Proteção contra SSRF: o webhook é uma URL escolhida pelo cliente e chamada pelo nosso servidor.
/// Na gravação, a URL precisa ser https e não apontar para rede interna; na entrega, o IP resolvido é
/// conferido de novo na hora de conectar (um DNS que muda depois do cadastro não passa).
/// </summary>
public static class WebhookUrls
{
    public static string? Validate(string? url, bool allowPrivateNetworks)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "Informe a URL que vai receber os eventos.";
        url = url.Trim();
        if (url.Length > WebhookEndpoint.UrlMaxLength)
            return $"Use no máximo {WebhookEndpoint.UrlMaxLength} caracteres.";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return "Informe uma URL completa, começando com https://.";
        if (uri.Scheme == Uri.UriSchemeHttp && !allowPrivateNetworks)
            return "Use https://: os eventos têm dados dos contratos e precisam ir criptografados.";
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return "Não coloque usuário e senha na URL; confira a assinatura do webhook no seu servidor.";
        if (!allowPrivateNetworks && IsInternalHost(uri))
            return "Essa URL aponta para uma rede interna. Use um endereço público.";
        return null;
    }

    private static bool IsInternalHost(Uri uri)
    {
        var host = uri.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IsPrivate(ip);
    }

    /// <summary>Loopback, redes privadas, link-local (inclui 169.254.169.254 dos metadados de nuvem), CGNAT e afins.</summary>
    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6None))
            return true;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] switch
            {
                0 or 10 or 127 => true,
                100 => b[1] >= 64 && b[1] <= 127,       // 100.64.0.0/10
                169 => b[1] == 254,                     // link-local
                172 => b[1] >= 16 && b[1] <= 31,
                192 => (b[1] == 168) || (b[1] == 0 && b[2] == 0),
                198 => b[1] is 18 or 19,                // testes de rede
                >= 224 => true,                         // multicast e reservados
                _ => false,
            };
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast
                || (b[0] & 0xfe) == 0xfc;              // fc00::/7 (unique local)
        }
        return true;
    }

    /// <summary>Conexão do HttpClient dos webhooks: resolve o nome e só conecta em IP público.</summary>
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> SafeConnect(bool allowPrivateNetworks) =>
        async (context, ct) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
            var allowed = allowPrivateNetworks ? addresses : addresses.Where(a => !IsPrivate(a)).ToArray();
            if (allowed.Length == 0)
                throw new HttpRequestException($"O endereço {host} aponta para uma rede interna e não recebe webhooks.");

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
}

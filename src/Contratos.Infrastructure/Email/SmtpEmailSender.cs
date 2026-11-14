using Contratos.Application.Email;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Contratos.Infrastructure.Email;

public sealed class SmtpOptions
{
    public const string Section = "Smtp";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public string? Username { get; set; }
    public string? Password { get; set; }
    /// <summary>None, StartTls ou SslOnConnect. Mailpit em dev usa None.</summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTlsWhenAvailable;
    public string FromAddress { get; set; } = "nao-responda@contratos.local";
    public string FromName { get; set; } = "Contratos";
}

internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(o.Host, o.Port, o.Security, ct);
        if (!string.IsNullOrEmpty(o.Username))
            await client.AuthenticateAsync(o.Username, o.Password ?? "", ct);
        await client.SendAsync(new SendRequest(mime), ct);
        await client.DisconnectAsync(true, ct);

        // Sem o endereço no log (LGPD): só o assunto.
        logger.LogInformation("E-mail enviado: {Subject}", message.Subject);
    }
}

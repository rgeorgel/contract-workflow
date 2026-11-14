namespace Contratos.Application.Email;

public sealed record EmailMessage(string To, string ToName, string Subject, string HtmlBody, string TextBody);

/// <summary>E-mails do próprio sistema (confirmação, convites, senha). Links de assinatura são da Autentique.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

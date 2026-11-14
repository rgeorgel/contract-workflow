namespace Contratos.Infrastructure.Email;

public enum EmailProvider
{
    /// <summary>SMTP comum (em dev, o Mailpit).</summary>
    Smtp,
    /// <summary>API HTTP da Resend (https://resend.com).</summary>
    Resend,
}

/// <summary>O que vale para qualquer provedor: quem envia e por onde.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    public EmailProvider Provider { get; set; } = EmailProvider.Smtp;
    /// <summary>Na Resend, precisa ser de um domínio verificado na conta.</summary>
    public string FromAddress { get; set; } = "nao-responda@contratos.local";
    public string FromName { get; set; } = "Contratos";
}

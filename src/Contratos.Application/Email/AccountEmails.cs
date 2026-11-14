using System.Net;

namespace Contratos.Application.Email;

public static class AccountEmails
{
    public static EmailMessage ConfirmEmail(string productName, string to, string name, string organizationName, string link) =>
        Build(productName, to, name,
            subject: $"Confirme seu e-mail para ativar {organizationName}",
            intro: $"Você criou a conta de <strong>{WebUtility.HtmlEncode(organizationName)}</strong> no {WebUtility.HtmlEncode(productName)}. Confirme seu e-mail para começar a usar.",
            introText: $"Você criou a conta de {organizationName} no {productName}. Confirme seu e-mail para começar a usar.",
            buttonLabel: "Confirmar e-mail",
            link: link,
            footer: "O link vale por 24 horas. Se não foi você, ignore esta mensagem.");

    public static EmailMessage AccountAlreadyExists(string productName, string to, string name, string loginLink) =>
        Build(productName, to, name,
            subject: $"Você já tem uma conta no {productName}",
            intro: "Alguém tentou criar uma nova conta com este e-mail, mas ele já está cadastrado. Para acessar, basta entrar.",
            introText: "Alguém tentou criar uma nova conta com este e-mail, mas ele já está cadastrado. Para acessar, basta entrar.",
            buttonLabel: "Entrar",
            link: loginLink,
            footer: "Se não foi você, ignore esta mensagem; nada mudou na sua conta.");

    public static EmailMessage Invite(string productName, string to, string name, string organizationName, string invitedBy, string link) =>
        Build(productName, to, name,
            subject: $"{invitedBy} convidou você para {organizationName}",
            intro: $"<strong>{WebUtility.HtmlEncode(invitedBy)}</strong> convidou você para usar o {WebUtility.HtmlEncode(productName)} na equipe de <strong>{WebUtility.HtmlEncode(organizationName)}</strong>. Crie sua senha para entrar.",
            introText: $"{invitedBy} convidou você para usar o {productName} na equipe de {organizationName}. Crie sua senha para entrar.",
            buttonLabel: "Aceitar convite",
            link: link,
            footer: "O convite vale por 7 dias. Se não esperava por ele, ignore esta mensagem.");

    public static EmailMessage ResetPassword(string productName, string to, string name, string link) =>
        Build(productName, to, name,
            subject: $"Redefina sua senha do {productName}",
            intro: "Recebemos um pedido para redefinir a sua senha. Clique no botão para escolher uma nova.",
            introText: "Recebemos um pedido para redefinir a sua senha. Use o link abaixo para escolher uma nova.",
            buttonLabel: "Redefinir senha",
            link: link,
            footer: "O link vale por 2 horas. Se não foi você, ignore esta mensagem; sua senha continua a mesma.");

    private static EmailMessage Build(string productName, string to, string name, string subject,
        string intro, string introText, string buttonLabel, string link, string footer)
    {
        var safeName = WebUtility.HtmlEncode(name);
        var safeLink = WebUtility.HtmlEncode(link);
        var safeProduct = WebUtility.HtmlEncode(productName);

        var html = $"""
            <!doctype html>
            <html lang="pt-BR"><body style="margin:0;background:#f4f5f7;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#1c2333">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="padding:32px 16px">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#ffffff;border-radius:12px;padding:32px">
                    <tr><td style="font-weight:700;font-size:18px;color:#2747d6;padding-bottom:24px">{safeProduct}</td></tr>
                    <tr><td style="font-size:16px;line-height:1.5;padding-bottom:8px">{(string.IsNullOrWhiteSpace(name) ? "Olá!" : $"Olá, {safeName}!")}</td></tr>
                    <tr><td style="font-size:16px;line-height:1.5;padding-bottom:24px">{intro}</td></tr>
                    <tr><td style="padding-bottom:24px">
                      <a href="{safeLink}" style="display:inline-block;background:#2747d6;color:#ffffff;text-decoration:none;font-weight:600;padding:12px 20px;border-radius:8px">{WebUtility.HtmlEncode(buttonLabel)}</a>
                    </td></tr>
                    <tr><td style="font-size:13px;line-height:1.5;color:#5b6478;padding-bottom:8px">Se o botão não funcionar, copie este endereço no navegador:<br><a href="{safeLink}" style="color:#2747d6;word-break:break-all">{safeLink}</a></td></tr>
                    <tr><td style="font-size:13px;line-height:1.5;color:#5b6478">{WebUtility.HtmlEncode(footer)}</td></tr>
                  </table>
                </td></tr>
              </table>
            </body></html>
            """;

        var text = $"""
            {(string.IsNullOrWhiteSpace(name) ? "Olá!" : $"Olá, {name}!")}

            {introText}

            {buttonLabel}: {link}

            {footer}
            """;

        return new EmailMessage(to, name, subject, html, text);
    }
}

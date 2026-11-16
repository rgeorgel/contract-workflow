using System.Globalization;
using System.Net;
using System.Text;

namespace Contratos.Application.Email;

/// <summary>E-mails sobre contratos para a equipe (não para quem assina).</summary>
public static class ContractEmails
{
    public sealed record AttentionLine(int Number, string Title, string Reason, IReadOnlyList<string> PendingSigners, string Link);

    public static string StaleReason(DateTimeOffset sentAt, DateTimeOffset now)
    {
        var days = Math.Max(1, (int)(now - sentAt).TotalDays);
        return $"aguardando assinatura há {days} {(days == 1 ? "dia" : "dias")}";
    }

    public static string ExpiringReason(DateTimeOffset expiresAt, DateTimeOffset now)
    {
        // Prazo vale até o fim do dia em Brasília.
        var brt = TimeSpan.FromHours(-3);
        var days = expiresAt.ToOffset(brt).Date.Subtract(now.ToOffset(brt).Date).Days;
        var date = expiresAt.ToOffset(brt).ToString("dd/MM", CultureInfo.InvariantCulture);
        return days switch
        {
            <= 0 => $"o prazo para assinar termina hoje ({date})",
            1 => $"o prazo para assinar termina amanhã ({date})",
            _ => $"o prazo para assinar termina em {days} dias ({date})",
        };
    }

    /// <summary>Resumo dos contratos que precisam de atenção (parados ou com prazo chegando).</summary>
    public static EmailMessage NeedsAttention(string productName, string to, string name, IReadOnlyList<AttentionLine> lines, string listLink)
    {
        var subject = lines.Count == 1
            ? $"Contrato nº {lines[0].Number:0000} precisa de atenção"
            : $"{lines.Count} contratos precisam de atenção";
        var html = new StringBuilder("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\">");
        var text = new StringBuilder();
        foreach (var l in lines)
        {
            var who = l.PendingSigners.Count > 0 ? $"Falta: {string.Join(", ", l.PendingSigners)}." : "";
            html.Append("<tr><td style=\"padding:12px 0;border-top:1px solid #e1e4ee;font-size:14px;line-height:1.5\">")
                .Append($"<a href=\"{WebUtility.HtmlEncode(l.Link)}\" style=\"color:#2747d6;font-weight:600;text-decoration:none\">Nº {l.Number:0000} · {WebUtility.HtmlEncode(l.Title)}</a><br>")
                .Append($"<span style=\"color:#1c2333\">{WebUtility.HtmlEncode(Capitalize(l.Reason))}.</span>")
                .Append(who.Length > 0 ? $" <span style=\"color:#5b6478\">{WebUtility.HtmlEncode(who)}</span>" : "")
                .Append("</td></tr>");
            text.Append($"- Nº {l.Number:0000} · {l.Title}: {l.Reason}. {who}\n  {l.Link}\n");
        }
        html.Append("</table>");

        var intro = lines.Count == 1 ? "Um contrato seu precisa de atenção:" : "Estes contratos seus precisam de atenção:";
        return AccountEmails.Build(productName, to, name, subject, intro, intro,
            buttonLabel: "Ver contratos",
            link: listLink,
            footer: "Os prazos e os dias para este aviso ficam em Configurações, \"Alertas e lembretes\".",
            detailHtml: html.ToString(),
            detailText: text.ToString());
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

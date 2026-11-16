using Contratos.Domain.Contracts;

namespace Contratos.Domain.Billing;

/// <summary>
/// Plano de assinatura. A cota de contratos vale por ciclo (o mês, ou o período pago da assinatura)
/// e não acumula; quem passa dela usa créditos avulsos, se tiver.
/// </summary>
/// <param name="MaxUsers">Usuários ativos mais convites pendentes; null = ilimitado.</param>
/// <param name="MaxTemplates">Templates não arquivados; null = ilimitado.</param>
public sealed record Plan(
    string Id,
    string Name,
    decimal MonthlyPrice,
    decimal AnnualPrice,
    int ContractsPerPeriod,
    int? MaxUsers,
    int? MaxTemplates,
    IReadOnlyList<DeliveryMethod> Channels)
{
    public bool Allows(DeliveryMethod channel) => Channels.Contains(channel);
}

/// <summary>Pacote de contratos avulsos, pago na hora; cada unidade envia um contrato.</summary>
public sealed record CreditPack(int Quantity, decimal Price)
{
    public decimal UnitPrice => Math.Round(Price / Quantity, 2);
}

public static class Plans
{
    /// <summary>Sem mensalidade: cada contrato usa 1 crédito. Quem se cadastra ganha os créditos de boas-vindas.</summary>
    public const string PayAsYouGoId = "avulso";
    /// <summary>Nome antigo do plano sem mensalidade (configurações e dados de antes do Avulso).</summary>
    private const string LegacyFreeId = "gratis";
    public const string EssentialId = "essencial";
    public const string ProfessionalId = "profissional";
    public const string BusinessId = "empresa";

    /// <summary>Créditos avulsos valem por 12 meses a partir da compra.</summary>
    public const int CreditValidityMonths = 12;

    /// <summary>Créditos de boas-vindas: liberados ao confirmar o e-mail do cadastro, valem 30 dias.</summary>
    public const int WelcomeCredits = 3;
    public const int WelcomeCreditDays = 30;

    public static readonly Plan PayAsYouGo = new(PayAsYouGoId, "Avulso", 0m, 0m, 0, 1, 2,
        [DeliveryMethod.Email, DeliveryMethod.WhatsApp, DeliveryMethod.Sms]);
    public static readonly Plan Essential = new(EssentialId, "Essencial", 39.90m, 399m, 20, 3, 10,
        [DeliveryMethod.Email, DeliveryMethod.WhatsApp]);
    public static readonly Plan Professional = new(ProfessionalId, "Profissional", 89.90m, 899m, 80, 10, null,
        [DeliveryMethod.Email, DeliveryMethod.WhatsApp, DeliveryMethod.Sms]);
    public static readonly Plan Business = new(BusinessId, "Empresa", 199.90m, 1999m, 250, null, null,
        [DeliveryMethod.Email, DeliveryMethod.WhatsApp, DeliveryMethod.Sms]);

    /// <summary>Do mais barato para o mais caro.</summary>
    public static IReadOnlyList<Plan> All { get; } = [PayAsYouGo, Essential, Professional, Business];

    public static IReadOnlyList<CreditPack> Packs { get; } =
    [
        new(1, 3.90m),
        new(5, 17.50m),
        new(10, 29.90m),
        new(25, 64.90m),
        new(50, 119.90m),
    ];

    public static Plan? Find(string? id) =>
        string.Equals(id, LegacyFreeId, StringComparison.OrdinalIgnoreCase)
            ? PayAsYouGo
            : All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Plano desconhecido (removido do catálogo) cai no Avulso, nunca num ilimitado.</summary>
    public static Plan Get(string? id) => Find(id) ?? PayAsYouGo;

    /// <summary>O plano de assinatura mais barato que libera o canal (o Avulso libera todos, mas não é um "upgrade").</summary>
    public static Plan CheapestWith(DeliveryMethod channel) =>
        All.FirstOrDefault(p => p.MonthlyPrice > 0 && p.Allows(channel)) ?? All.First(p => p.Allows(channel));
}

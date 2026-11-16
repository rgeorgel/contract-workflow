using Contratos.Application.Common;
using Contratos.Domain.Billing;
using Contratos.Domain.Contracts;

namespace Contratos.Application.Billing;

public sealed record PlanDto(
    string Id, string Name, decimal MonthlyPrice, decimal AnnualPrice, int ContractsPerPeriod,
    int? MaxUsers, int? MaxTemplates, IReadOnlyList<DeliveryMethod> Channels)
{
    public static PlanDto From(Plan p) =>
        new(p.Id, p.Name, p.MonthlyPrice, p.AnnualPrice, p.ContractsPerPeriod, p.MaxUsers, p.MaxTemplates, p.Channels);
}

public sealed record CreditPackDto(int Quantity, decimal Price, decimal UnitPrice);

/// <summary>Catálogo público (página inicial e Configurações), sempre a partir de <see cref="Domain.Billing.Plans"/>.</summary>
public sealed record PlanCatalogDto(IReadOnlyList<PlanDto> Plans, IReadOnlyList<CreditPackDto> Packs, int CreditValidityMonths)
{
    public static PlanCatalogDto Current { get; } = new(
        Domain.Billing.Plans.All.Select(PlanDto.From).ToList(),
        Domain.Billing.Plans.Packs.Select(p => new CreditPackDto(p.Quantity, p.Price, p.UnitPrice)).ToList(),
        Domain.Billing.Plans.CreditValidityMonths);
}

public sealed record CreditGrantDto(Guid Id, int Quantity, int Remaining, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string Source, string? Note);

/// <param name="Status">Status do Stripe (active, trialing, past_due, canceled...).</param>
/// <param name="Interval">"month" ou "year".</param>
/// <param name="CancelAt">Cancelamento agendado: o plano vale até esta data.</param>
public sealed record SubscriptionDto(string Status, string? Interval, DateTimeOffset? CancelAt, DateTimeOffset? CurrentPeriodEnd);

/// <param name="ContractsUsed">Da cota do plano, neste ciclo.</param>
/// <param name="ContractsFromCredits">Enviados com crédito avulso neste ciclo.</param>
/// <param name="AvailableSends">Quantos contratos ainda dá para enviar agora (cota restante + créditos).</param>
public sealed record BillingDto(
    PlanDto Plan,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int ContractsUsed,
    int ContractsLimit,
    int ContractsFromCredits,
    int CreditsAvailable,
    int AvailableSends,
    int UsersUsed,
    int? UsersLimit,
    int TemplatesUsed,
    int? TemplatesLimit,
    IReadOnlyList<CreditGrantDto> Credits,
    IReadOnlyList<PlanDto> Plans,
    IReadOnlyList<CreditPackDto> Packs,
    string? ContactEmail,
    SubscriptionDto? Subscription,
    bool OnlineCheckout,
    bool PaymentOverdue);

public interface IBillingService
{
    /// <summary>Plano, uso do ciclo e créditos da organização atual.</summary>
    Task<BillingDto> GetAsync(CancellationToken ct);
}

// ---------- Assinatura online (Stripe) ----------

/// <param name="Interval">"month" ou "year".</param>
public sealed record CheckoutRequest(string? Plan, string? Interval);
public sealed record ConfirmCheckoutRequest(string? SessionId);
public sealed record RedirectDto(string Url);
public sealed record BuyPackRequest(int? Quantity);

public interface ISubscriptionService
{
    /// <summary>Página de pagamento do Stripe (só cartão) para assinar o plano.</summary>
    Task<Result<RedirectDto>> CheckoutAsync(CheckoutRequest request, CancellationToken ct);
    /// <summary>Na volta do pagamento: confere a sessão e ativa o plano na hora, sem esperar o webhook.</summary>
    Task<Result<BillingDto>> ConfirmCheckoutAsync(ConfirmCheckoutRequest request, CancellationToken ct);
    /// <summary>Portal do Stripe: trocar de plano, trocar cartão, cancelar, ver faturas.</summary>
    Task<Result<RedirectDto>> PortalAsync(CancellationToken ct);
    /// <summary>Pagamento único (só cartão) de um pacote de contratos avulsos.</summary>
    Task<Result<RedirectDto>> BuyPackAsync(BuyPackRequest request, CancellationToken ct);
    /// <summary>Na volta do pagamento do pacote: confere a sessão e libera os créditos na hora.</summary>
    Task<Result<BillingDto>> ConfirmPackAsync(ConfirmCheckoutRequest request, CancellationToken ct);
}

// ---------- Operação da plataforma (sem tenant) ----------

public sealed record PlatformOrganizationItem(
    Guid Id, string Name, string Slug, string Plan, DateTimeOffset? PlanPeriodEnd,
    int ContractsUsed, int ContractsLimit, int CreditsAvailable, DateTimeOffset CreatedAt);

public sealed record ChangePlanRequest(string? Plan, DateTimeOffset? PeriodStart, DateTimeOffset? PeriodEnd);

/// <param name="Reference">Opcional e único por organização: repetir o mesmo pedido não duplica créditos.</param>
public sealed record GrantCreditsRequest(int? Quantity, string? Note, string? Reference, int? ValidityMonths);

public interface IPlatformBillingService
{
    Task<IReadOnlyList<PlatformOrganizationItem>> ListAsync(string? query, CancellationToken ct);
    Task<Result<BillingDto>> ChangePlanAsync(string slug, ChangePlanRequest request, CancellationToken ct);
    Task<Result<BillingDto>> GrantCreditsAsync(string slug, GrantCreditsRequest request, CancellationToken ct);
}

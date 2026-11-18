using Contratos.Application.Billing;
using Contratos.Application.Common;

namespace Contratos.Application.Platform;

/// <summary>
/// Situação da conta no portal: <c>suspended</c> (pela plataforma), <c>past_due</c>, <c>trialing</c>, <c>active</c>
/// (assinatura paga), <c>manual</c> (plano pago definido pela equipe, sem Stripe), <c>canceled</c> (teve assinatura
/// e voltou ao Avulso) ou <c>payg</c> (Avulso, nunca assinou).
/// </summary>
public static class TenantStatuses
{
    public const string Suspended = "suspended";
    public const string PastDue = "past_due";
    public const string Trialing = "trialing";
    public const string Active = "active";
    public const string Manual = "manual";
    public const string Canceled = "canceled";
    public const string PayAsYouGo = "payg";

    public static IReadOnlyList<string> All { get; } = [Active, Trialing, PastDue, Manual, Canceled, PayAsYouGo, Suspended];
}

/// <summary>Contratos por status (Abertos = enviados e parcialmente assinados).</summary>
public sealed record ContractCounts(int Total, int Draft, int Open, int Completed, int Declined, int Expired, int Cancelled, int Last30Days);

public sealed record TenantUsage(
    int ContractsUsed, int ContractsLimit, int CreditsAvailable,
    int UsersUsed, int? UsersLimit, int TemplatesUsed, int? TemplatesLimit,
    DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd);

public sealed record TenantIntegrations(int ApiKeys, int Webhooks, int EmbedOrigins);

/// <param name="Owner">Primeiro Admin da conta (quem cadastrou), para contato.</param>
public sealed record TenantOwner(string Name, string Email);

public sealed record TenantListItem(
    Guid Id, string Name, string Slug, DateTimeOffset CreatedAt,
    string PlanId, string PlanName, string Status, string? SubscriptionInterval,
    DateTimeOffset? SubscriptionCancelAt, DateTimeOffset? SuspendedAt,
    TenantUsage Usage, ContractCounts Contracts, TenantIntegrations Integrations,
    DateTimeOffset? LastSeenAt, TenantOwner? Owner);

public sealed record TenantListQuery(
    string? Q, string? Plan, string? Status, string? Activity, string? Sort, int Page = 1, int PageSize = 24);

/// <param name="ImpersonationBlockedReason">Por que não dá para "Entrar como" esta pessoa; null quando dá.</param>
public sealed record TenantUserDto(
    Guid Id, string Name, string Email, string Role, string Status, bool EmailConfirmed,
    DateTimeOffset CreatedAt, DateTimeOffset? LastSeenAt, string? ImpersonationBlockedReason);

public sealed record TemplateUsageDto(Guid Id, string Name, bool Archived, int Contracts, DateTimeOffset UpdatedAt);

public sealed record AuditEntryDto(string Action, string ActorName, string? Details, DateTimeOffset CreatedAt);

/// <param name="StripeCustomerId">Para abrir o cliente no painel do Stripe.</param>
public sealed record TenantDetailDto(
    TenantListItem Summary,
    string? SuspensionReason,
    string? StripeCustomerId,
    BillingDto Billing,
    IReadOnlyList<TenantUserDto> Users,
    IReadOnlyList<TemplateUsageDto> Templates,
    IReadOnlyList<AuditEntryDto> Audit);

/// <summary>
/// Um grupo da linha do tempo: tantos eventos do mesmo tipo no mesmo dia (horário de Brasília).
/// <paramref name="Actors"/> são os nomes de quem fez (até 3); <paramref name="Samples"/>, títulos de exemplo.
/// </summary>
public sealed record TimelineGroupDto(
    DateOnly Day, string Category, string Type, int Count, DateTimeOffset LastAt,
    IReadOnlyList<string> Actors, IReadOnlyList<string> Samples);

public static class TimelineCategories
{
    public const string Account = "conta";
    public const string Contracts = "contratos";
    public const string Signatures = "assinaturas";
    public const string Templates = "templates";
    public const string Billing = "plano";
    public const string Integrations = "integracoes";
    public const string Platform = "superadmin";

    public static IReadOnlyList<string> All { get; } = [Account, Contracts, Signatures, Templates, Billing, Integrations, Platform];
}

public sealed record DailyCount(DateOnly Day, int Count);
public sealed record KeyCount(string Key, int Count);

/// <param name="Mrr">Receita mensal recorrente estimada pelos preços de tabela (anual dividido por 12).</param>
public sealed record PlatformOverviewDto(
    int Tenants, int NewLast7Days, int NewLast30Days, int ActiveLast7Days, int InactiveOver30Days,
    decimal Mrr, int PaidSubscriptions,
    int ContractsThisMonth, int ContractsLastMonth, int CompletedThisMonth,
    IReadOnlyList<KeyCount> ByPlan, IReadOnlyList<KeyCount> ByStatus,
    IReadOnlyList<DailyCount> SignupsByDay, IReadOnlyList<DailyCount> ContractsByDay,
    IReadOnlyList<TenantListItem> NeedsAttention, IReadOnlyList<TenantListItem> Newest);

public sealed record SuspendRequest(string? Reason);

/// <summary>Portal super admin: acompanhar e gerenciar todas as organizações (sem tenant).</summary>
public interface ISuperAdminService
{
    Task<PlatformOverviewDto> OverviewAsync(CancellationToken ct);
    Task<Paged<TenantListItem>> ListAsync(TenantListQuery query, CancellationToken ct);
    Task<Result<TenantDetailDto>> GetAsync(Guid id, CancellationToken ct);
    Task<Result<IReadOnlyList<TimelineGroupDto>>> TimelineAsync(Guid id, string? category, int days, CancellationToken ct);
    Task<Result<TenantDetailDto>> ChangePlanAsync(Guid id, ChangePlanRequest request, CancellationToken ct);
    Task<Result<TenantDetailDto>> GrantCreditsAsync(Guid id, GrantCreditsRequest request, CancellationToken ct);
    Task<Result<TenantDetailDto>> SuspendAsync(Guid id, SuspendRequest request, CancellationToken ct);
    Task<Result<TenantDetailDto>> ReactivateAsync(Guid id, CancellationToken ct);
}

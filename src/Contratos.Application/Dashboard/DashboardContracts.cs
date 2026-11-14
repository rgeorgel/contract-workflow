using Contratos.Domain.Contracts;

namespace Contratos.Application.Dashboard;

public sealed record StaleContract(Guid Id, int Number, string Title, DateTimeOffset SentAt, int Days, int SignersSigned, int SignersTotal);
public sealed record RecentContract(Guid Id, int Number, string Title, DateTimeOffset CompletedAt);

public sealed record DashboardSummary(
    IReadOnlyDictionary<string, int> Counts,
    int Open, int SendErrors, int SentThisMonth, int CompletedThisMonth, int StaleDays,
    IReadOnlyList<StaleContract> Stale, IReadOnlyList<RecentContract> RecentlyCompleted,
    bool HasPublishedTemplate);

public sealed record MemberDto(Guid Id, string Name);

public interface IDashboardService
{
    /// <param name="staleDays">Enviados há mais que isso, sem concluir, entram em "pendentes antigos".</param>
    Task<DashboardSummary> GetSummaryAsync(int staleDays, CancellationToken ct);
    Task<IReadOnlyList<MemberDto>> MembersAsync(CancellationToken ct);
}

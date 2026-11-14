using Contratos.Application.Common;
using Contratos.Application.Dashboard;
using Contratos.Domain.Contracts;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Infrastructure.Dashboard;

internal sealed class DashboardService(AppDbContext db, ICurrentTenant tenant, TimeProvider clock) : IDashboardService
{
    public async Task<DashboardSummary> GetSummaryAsync(int staleDays, CancellationToken ct)
    {
        staleDays = Math.Clamp(staleDays, 1, 365);
        var now = clock.GetUtcNow();
        // Mês corrente no horário de Brasília.
        var brt = now.ToOffset(TimeSpan.FromHours(-3));
        var monthStart = new DateTimeOffset(brt.Year, brt.Month, 1, 0, 0, 0, TimeSpan.FromHours(-3)).ToUniversalTime();
        var staleBefore = now.AddDays(-staleDays);

        var counts = await db.Contracts.GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        foreach (var status in Enum.GetValues<ContractStatus>())
            counts.TryAdd(status, 0);
        var countsByName = counts.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);

        var open = db.Contracts.Where(c => c.Status == ContractStatus.Sent || c.Status == ContractStatus.PartiallySigned);
        var stale = await open
            .Where(c => c.SentAt != null && c.SentAt < staleBefore)
            .OrderBy(c => c.SentAt)
            .Take(10)
            .Select(c => new
            {
                c.Id, c.Number, c.Title, SentAt = c.SentAt!.Value,
                Signed = c.Signers.Count(s => s.Status == SignerStatus.Signed),
                Total = c.Signers.Count,
            })
            .ToListAsync(ct);

        var recent = await db.Contracts
            .Where(c => c.Status == ContractStatus.Completed && c.CompletedAt != null)
            .OrderByDescending(c => c.CompletedAt)
            .Take(5)
            .Select(c => new RecentContract(c.Id, c.Number, c.Title, c.CompletedAt!.Value))
            .ToListAsync(ct);

        return new DashboardSummary(
            countsByName,
            Open: counts[ContractStatus.Sent] + counts[ContractStatus.PartiallySigned],
            SendErrors: await open.CountAsync(c => c.SendError != null, ct),
            SentThisMonth: await db.Contracts.CountAsync(c => c.SentAt >= monthStart, ct),
            CompletedThisMonth: await db.Contracts.CountAsync(c => c.CompletedAt >= monthStart, ct),
            StaleDays: staleDays,
            Stale: stale.Select(s => new StaleContract(s.Id, s.Number, s.Title, s.SentAt, (int)(now - s.SentAt).TotalDays, s.Signed, s.Total)).ToList(),
            RecentlyCompleted: recent,
            HasPublishedTemplate: await db.Templates.AnyAsync(t => t.CurrentVersionId != null && t.ArchivedAt == null, ct));
    }

    public async Task<IReadOnlyList<MemberDto>> MembersAsync(CancellationToken ct)
    {
        var orgId = tenant.RequireOrganizationId();
        return await db.Users
            .Where(u => u.OrganizationId == orgId && u.PasswordHash != null)
            .OrderBy(u => u.Name)
            .Select(u => new MemberDto(u.Id, u.Name))
            .ToListAsync(ct);
    }
}

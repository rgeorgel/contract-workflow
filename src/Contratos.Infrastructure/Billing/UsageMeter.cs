using Contratos.Application.Common;
using Contratos.Domain.Billing;
using Contratos.Domain.Contracts;
using Contratos.Domain.Organizations;
using Contratos.Infrastructure.Messaging;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Infrastructure.Billing;

public sealed class BillingOptions
{
    public const string Section = "Billing";

    /// <summary>Plano de quem cria conta. Em produção, o grátis.</summary>
    public string DefaultPlan { get; set; } = Plans.FreeId;

    /// <summary>Para onde mandar quem quer mudar de plano ou comprar contratos, enquanto não há checkout.</summary>
    public string? ContactEmail { get; set; }
}

/// <summary>
/// Cota e créditos de uma organização. O envio consome primeiro a cota do plano no ciclo e, quando
/// ela acaba, o crédito avulso que vence antes. Tudo com a linha da organização travada, para dois
/// envios ao mesmo tempo não usarem a mesma vaga.
/// </summary>
internal sealed class UsageMeter(AppDbContext db, TimeProvider clock)
{
    public sealed record Snapshot(Plan Plan, DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd, int PlanUsed, int CreditUsed, int CreditsAvailable)
    {
        public int PlanRemaining => Math.Max(0, Plan.ContractsPerPeriod - PlanUsed);
        public int AvailableSends => PlanRemaining + CreditsAvailable;
    }

    public async Task<Snapshot> SnapshotAsync(Organization org, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var (start, end) = org.PeriodAt(now);
        var used = await db.UsageRecords
            .Where(u => u.OrganizationId == org.Id && u.RefundedAt == null && u.ConsumedAt >= start && u.ConsumedAt < end)
            .GroupBy(u => u.Source)
            .Select(g => new { Source = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var credits = await db.CreditGrants
            .Where(g => g.OrganizationId == org.Id && g.Remaining > 0 && g.ExpiresAt > now)
            .SumAsync(g => (int?)g.Remaining, ct) ?? 0;
        return new Snapshot(org.Plan, start, end,
            used.Where(u => u.Source == UsageSource.Plan).Sum(u => u.Count),
            used.Where(u => u.Source == UsageSource.Credit).Sum(u => u.Count),
            credits);
    }

    /// <summary>Checagem rápida, sem trava, para recusar antes de gerar o PDF.</summary>
    public async Task<Error?> PrecheckSendAsync(Organization org, CancellationToken ct)
    {
        var snapshot = await SnapshotAsync(org, ct);
        return snapshot.AvailableSends > 0 ? null : QuotaExceeded(snapshot);
    }

    /// <summary>
    /// Reserva um envio para o contrato. Precisa estar dentro de uma transação: a trava da organização
    /// vale até o commit. Os registros entram no próximo SaveChanges (o crédito já sai na hora).
    /// </summary>
    public async Task<Result<UsageSource>> ConsumeAsync(Organization org, Guid contractId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("O consumo de cota precisa de uma transação.");

        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM organizations WHERE id = {org.Id} FOR UPDATE", ct);
        var snapshot = await SnapshotAsync(org, ct);
        var now = clock.GetUtcNow();

        if (snapshot.PlanRemaining > 0)
        {
            db.UsageRecords.Add(new UsageRecord
            {
                OrganizationId = org.Id, ContractId = contractId, PlanId = snapshot.Plan.Id,
                Source = UsageSource.Plan, ConsumedAt = now,
            });
            var used = snapshot.PlanUsed + 1;
            var limit = snapshot.Plan.ContractsPerPeriod;
            if (used == limit || used == WarningAt(limit))
                db.Enqueue(OutboxTypes.UsageAlert, new UsageAlertMessage(org.Id, snapshot.Plan.Id, used, limit, snapshot.PeriodEnd));
            return UsageSource.Plan;
        }

        // Crédito que vence antes; decremento atômico no banco.
        var grantIds = await db.Database.SqlQuery<Guid>($"""
            UPDATE credit_grants SET remaining = remaining - 1
            WHERE id = (
                SELECT id FROM credit_grants
                WHERE organization_id = {org.Id} AND remaining > 0 AND expires_at > {now}
                ORDER BY expires_at, created_at
                LIMIT 1
                FOR UPDATE)
            RETURNING id AS "Value"
            """).ToListAsync(ct);
        if (grantIds.Count == 0)
            return QuotaExceeded(snapshot);

        db.UsageRecords.Add(new UsageRecord
        {
            OrganizationId = org.Id, ContractId = contractId, PlanId = snapshot.Plan.Id,
            Source = UsageSource.Credit, CreditGrantId = grantIds[0], ConsumedAt = now,
        });
        return UsageSource.Credit;
    }

    /// <summary>
    /// Envio desfeito antes de chegar ao provedor (o provedor não cobrou): o contrato sai da contagem
    /// e o crédito, se foi um, volta. Também precisa de transação, para não devolver duas vezes.
    /// </summary>
    public async Task RefundAsync(Guid contractId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A devolução de cota precisa de uma transação.");

        var record = (await db.UsageRecords
            .FromSql($"SELECT * FROM usage_records WHERE contract_id = {contractId} AND refunded_at IS NULL ORDER BY id DESC LIMIT 1 FOR UPDATE")
            .ToListAsync(ct)).SingleOrDefault();
        if (record is null)
            return;
        record.RefundedAt = clock.GetUtcNow();
        if (record.CreditGrantId is { } grantId)
            await db.Database.ExecuteSqlAsync(
                $"UPDATE credit_grants SET remaining = remaining + 1 WHERE id = {grantId} AND remaining < quantity", ct);
    }

    /// <summary>Aviso de 80%; em cotas pequenas, em que 80% arredonda para o total, só o de 100%.</summary>
    internal static int WarningAt(int limit)
    {
        var at = (int)Math.Ceiling(limit * 0.8);
        return at >= limit ? -1 : at;
    }

    public static Error QuotaExceeded(Snapshot s)
    {
        var renews = s.PeriodEnd.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM");
        return Error.Conflict(
            $"Você já enviou os {s.Plan.ContractsPerPeriod} contratos do plano {s.Plan.Name} neste ciclo, que renova em {renews}, "
            + "e não tem créditos avulsos. Compre um pacote de contratos ou mude de plano em Configurações.",
            "quota_exceeded");
    }

    public static string ChannelLabel(DeliveryMethod m) => m switch
    {
        DeliveryMethod.WhatsApp => "WhatsApp",
        DeliveryMethod.Sms => "SMS",
        _ => "E-mail",
    };

    public static string ChannelNotInPlan(Plan plan, DeliveryMethod m) =>
        $"Envio por {ChannelLabel(m)} não faz parte do plano {plan.Name}. Ele está disponível a partir do plano {Plans.CheapestWith(m).Name}.";

    // ---------- Usuários e templates ----------

    public async Task<int> CountSeatsAsync(Guid organizationId, CancellationToken ct) =>
        await db.Users.CountAsync(u => u.OrganizationId == organizationId && u.DeactivatedAt == null, ct);

    /// <summary>
    /// Templates ativos. O filtro de organização vai explícito: assim a contagem funciona com e sem tenant
    /// (operação da plataforma), e o IgnoreQueryFilters não abre nada além desta organização.
    /// </summary>
    public async Task<int> CountTemplatesAsync(Guid organizationId, CancellationToken ct) =>
        await db.Templates.IgnoreQueryFilters().CountAsync(t => t.OrganizationId == organizationId && t.ArchivedAt == null, ct);

    /// <summary>Mais um usuário (convite ou reativação) cabe no plano?</summary>
    public async Task<Error?> CheckSeatAsync(Guid organizationId, CancellationToken ct)
    {
        var plan = await PlanOfAsync(organizationId, ct);
        if (plan.MaxUsers is not { } max || await CountSeatsAsync(organizationId, ct) < max)
            return null;
        return Error.Conflict(
            $"O plano {plan.Name} permite {max} {(max == 1 ? "usuário" : "usuários")}, contando convites pendentes. "
            + "Desative alguém ou mude de plano para adicionar mais.",
            "user_limit");
    }

    /// <summary>Mais um template ativo (criar, duplicar, desarquivar) cabe no plano?</summary>
    public async Task<Error?> CheckTemplateAsync(Guid organizationId, CancellationToken ct)
    {
        var plan = await PlanOfAsync(organizationId, ct);
        if (plan.MaxTemplates is not { } max || await CountTemplatesAsync(organizationId, ct) < max)
            return null;
        return Error.Conflict(
            $"O plano {plan.Name} permite {max} templates ativos. Arquive um template ou mude de plano para criar outro.",
            "template_limit");
    }

    public async Task<Plan> PlanOfAsync(Guid organizationId, CancellationToken ct) =>
        Plans.Get(await db.Organizations.Where(o => o.Id == organizationId).Select(o => o.PlanId).SingleAsync(ct));
}

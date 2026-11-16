using Contratos.Application.Integrations;
using Contratos.Domain.Billing;
using Contratos.Domain.Contracts;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Infrastructure.Integrations;

/// <summary>
/// Transforma eventos novos da linha do tempo (<see cref="ContractEvent"/>) em eventos de webhook, no mesmo
/// SaveChanges que os criou: se a mudança é gravada, o aviso também é; se não, nenhum dos dois.
/// Cobre a tela, a API pública, o worker (webhooks do provedor, expiração) e os alertas sem cada um precisar lembrar.
/// </summary>
internal static class WebhookEventWriter
{
    public static async Task CollectAsync(AppDbContext db, string? apiKeyName, CancellationToken ct)
    {
        var added = db.ChangeTracker.Entries<ContractEvent>()
            .Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
        if (added.Count == 0)
            return;

        // Linha do tempo: o que veio da API mostra a chave que fez.
        if (apiKeyName is not null)
            foreach (var e in added)
                e.Payload.TryAdd("apiKey", apiKeyName);

        var mapped = added
            .Select(e => (Event: e, Type: WebhookEventTypes.FromContractEvent(e.Type)))
            .Where(x => x.Type is not null)
            .ToList();
        if (mapped.Count == 0)
            return;

        var contracts = new Dictionary<Guid, Contract>();
        foreach (var id in mapped.Select(m => m.Event.ContractId).Distinct())
        {
            var contract = db.ChangeTracker.Entries<Contract>().FirstOrDefault(c => c.Entity.Id == id)?.Entity
                // Só pelo id de um evento que acabou de ser criado para ele: não vaza outra organização.
                ?? await db.Contracts.IgnoreQueryFilters().AsNoTracking().Include(c => c.Signers)
                    .SingleOrDefaultAsync(c => c.Id == id, ct);
            if (contract is not null)
                contracts[id] = contract;
        }
        if (contracts.Count == 0)
            return;

        var orgIds = contracts.Values.Select(c => c.OrganizationId).Distinct().ToList();
        var endpoints = await db.WebhookEndpoints.AsNoTracking()
            .Where(w => orgIds.Contains(w.OrganizationId) && w.Enabled)
            .Select(w => new { w.Id, w.OrganizationId, w.Events })
            .ToListAsync(ct);
        if (endpoints.Count == 0)
            return;
        var plans = await db.Organizations.Where(o => orgIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.PlanId, ct);

        var versionIds = contracts.Values.Select(c => c.TemplateVersionId).Distinct().ToList();
        var templates = await db.TemplateVersions.Where(v => versionIds.Contains(v.Id))
            .Join(db.Templates.IgnoreQueryFilters(), v => v.TemplateId, t => t.Id,
                (v, t) => new { VersionId = v.Id, t.Id, t.Name, v.Version })
            .ToDictionaryAsync(x => x.VersionId, x => new PublicTemplateRef(x.Id, x.Name, x.Version), ct);

        foreach (var (ev, type) in mapped)
        {
            if (!contracts.TryGetValue(ev.ContractId, out var contract))
                continue;
            // Plano sem API (mudou de plano): os endpoints ficam guardados, mas param de receber.
            if (!Plans.Get(plans.GetValueOrDefault(contract.OrganizationId)).ApiAccess)
                continue;
            var targets = endpoints.Where(w => w.OrganizationId == contract.OrganizationId
                && (w.Events.Count == 0 || w.Events.Contains(type!))).ToList();
            if (targets.Count == 0)
                continue;

            var signer = ev.SignerId is { } signerId ? contract.Signers.FirstOrDefault(s => s.Id == signerId) : null;
            var reason = ev.Payload.GetValueOrDefault("reason") ?? ev.Payload.GetValueOrDefault("error");
            var template = templates.GetValueOrDefault(contract.TemplateVersionId) ?? new PublicTemplateRef(Guid.Empty, "—", 0);
            var webhookEvent = new WebhookEvent
            {
                OrganizationId = contract.OrganizationId,
                Type = type!,
                ContractId = contract.Id,
                CreatedAt = ev.OccurredAt,
            };
            webhookEvent.Payload = PublicJson.Serialize(new WebhookPayload(webhookEvent.Id, type!, ev.OccurredAt,
                new WebhookData(PublicMapper.Contract(contract, template), signer is null ? null : PublicMapper.Signer(signer), reason)));
            db.WebhookEvents.Add(webhookEvent);
            foreach (var target in targets)
                db.WebhookDeliveries.Add(new WebhookDelivery
                {
                    OrganizationId = contract.OrganizationId,
                    EventId = webhookEvent.Id,
                    EndpointId = target.Id,
                });
        }
    }
}

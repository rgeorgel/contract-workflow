using Contratos.Application.Integrations;
using Contratos.Domain.Billing;
using Contratos.Domain.Contracts;
using Contratos.Domain.Templates;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Infrastructure.Integrations;

/// <summary>
/// Gera os eventos de webhook no mesmo SaveChanges da mudança: se a mudança é gravada, o aviso também é; se não,
/// nenhum dos dois. Contratos: a partir dos eventos novos da linha do tempo (<see cref="ContractEvent"/>).
/// Templates: versão que acabou de ser publicada e template que acabou de ser arquivado.
/// Cobre a tela, a API pública, o embed, o worker (webhooks do provedor, expiração) e os alertas sem cada um precisar lembrar.
/// </summary>
internal static class WebhookEventWriter
{
    private sealed record Pending(Guid OrganizationId, string Type, Guid? ContractId, DateTimeOffset OccurredAt, Func<WebhookData> Data);

    public static async Task CollectAsync(AppDbContext db, string? apiKeyName, CancellationToken ct)
    {
        var pending = new List<Pending>();
        await CollectContractEventsAsync(db, apiKeyName, pending, ct);
        await CollectTemplateEventsAsync(db, pending, ct);
        if (pending.Count == 0)
            return;

        var orgIds = pending.Select(p => p.OrganizationId).Distinct().ToList();
        var endpoints = await db.WebhookEndpoints.AsNoTracking()
            .Where(w => orgIds.Contains(w.OrganizationId) && w.Enabled)
            .Select(w => new { w.Id, w.OrganizationId, w.Events })
            .ToListAsync(ct);
        if (endpoints.Count == 0)
            return;
        var plans = await db.Organizations.Where(o => orgIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.PlanId, ct);

        foreach (var p in pending)
        {
            // Plano sem API (mudou de plano): os endpoints ficam guardados, mas param de receber.
            if (!Plans.Get(plans.GetValueOrDefault(p.OrganizationId)).ApiAccess)
                continue;
            var targets = endpoints.Where(w => w.OrganizationId == p.OrganizationId
                && (w.Events.Count == 0 || w.Events.Contains(p.Type))).ToList();
            if (targets.Count == 0)
                continue;

            var webhookEvent = new WebhookEvent
            {
                OrganizationId = p.OrganizationId,
                Type = p.Type,
                ContractId = p.ContractId,
                CreatedAt = p.OccurredAt,
            };
            webhookEvent.Payload = PublicJson.Serialize(new WebhookPayload(webhookEvent.Id, p.Type, p.OccurredAt, p.Data()));
            db.WebhookEvents.Add(webhookEvent);
            foreach (var target in targets)
                db.WebhookDeliveries.Add(new WebhookDelivery
                {
                    OrganizationId = p.OrganizationId,
                    EventId = webhookEvent.Id,
                    EndpointId = target.Id,
                });
        }
    }

    private static async Task CollectContractEventsAsync(AppDbContext db, string? apiKeyName, List<Pending> pending, CancellationToken ct)
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

        var versionIds = contracts.Values.Select(c => c.TemplateVersionId).Distinct().ToList();
        var templates = await db.TemplateVersions.Where(v => versionIds.Contains(v.Id))
            .Join(db.Templates.IgnoreQueryFilters(), v => v.TemplateId, t => t.Id,
                (v, t) => new { VersionId = v.Id, t.Id, t.Name, v.Version })
            .ToDictionaryAsync(x => x.VersionId, x => new PublicTemplateRef(x.Id, x.Name, x.Version), ct);

        foreach (var (ev, type) in mapped)
        {
            if (!contracts.TryGetValue(ev.ContractId, out var contract))
                continue;
            var signer = ev.SignerId is { } signerId ? contract.Signers.FirstOrDefault(s => s.Id == signerId) : null;
            var reason = ev.Payload.GetValueOrDefault("reason") ?? ev.Payload.GetValueOrDefault("error");
            var template = templates.GetValueOrDefault(contract.TemplateVersionId) ?? new PublicTemplateRef(Guid.Empty, "—", 0);
            pending.Add(new Pending(contract.OrganizationId, type!, contract.Id, ev.OccurredAt,
                () => new WebhookData(PublicMapper.Contract(contract, template), signer is null ? null : PublicMapper.Signer(signer), reason)));
        }
    }

    private static async Task CollectTemplateEventsAsync(AppDbContext db, List<Pending> pending, CancellationToken ct)
    {
        var published = db.ChangeTracker.Entries<TemplateVersion>()
            .Where(e => e.Entity.PublishedAt is not null
                && (e.State == EntityState.Added || (e.State == EntityState.Modified && e.Property(v => v.PublishedAt).OriginalValue is null)))
            .Select(e => e.Entity).ToList();
        var archived = db.ChangeTracker.Entries<Template>()
            .Where(e => e.State == EntityState.Modified && e.Entity.ArchivedAt is not null && e.Property(t => t.ArchivedAt).OriginalValue is null)
            .Select(e => e.Entity).ToList();
        if (published.Count == 0 && archived.Count == 0)
            return;

        foreach (var version in published)
        {
            var template = db.ChangeTracker.Entries<Template>().FirstOrDefault(t => t.Entity.Id == version.TemplateId)?.Entity
                ?? await db.Templates.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(t => t.Id == version.TemplateId, ct);
            if (template is null)
                continue;
            pending.Add(new Pending(template.OrganizationId, WebhookEventTypes.TemplatePublished, null, version.PublishedAt!.Value,
                () => new WebhookData(null, null, null, Template: PublicMapper.Template(template, version))));
        }

        foreach (var template in archived)
        {
            var currentId = template.CurrentVersionId;
            var current = currentId is null ? null
                : db.ChangeTracker.Entries<TemplateVersion>().FirstOrDefault(v => v.Entity.Id == currentId)?.Entity
                    ?? await db.TemplateVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == currentId, ct);
            pending.Add(new Pending(template.OrganizationId, WebhookEventTypes.TemplateArchived, null, template.ArchivedAt!.Value,
                () => new WebhookData(null, null, null, Template: current is null
                    ? new PublicTemplate(template.Id, template.Name, template.Description, 0, [], [], template.UpdatedAt)
                    : PublicMapper.Template(template, current))));
        }
    }
}

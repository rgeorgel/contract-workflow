using Contratos.Application.Common;
using Contratos.Application.Templates;
using Contratos.Domain.Templates;
using Contratos.Infrastructure.Billing;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Infrastructure.Templates;

internal sealed class TemplateService(
    AppDbContext db,
    ICurrentTenant tenant,
    IHtmlSanitizer sanitizer,
    DocumentRenderer renderer,
    UsageMeter meter) : ITemplateService
{
    private static readonly Error NotFound = Error.NotFound("Template não encontrado.");

    public async Task<IReadOnlyList<TemplateListItem>> ListAsync(bool archived, CancellationToken ct)
    {
        var templates = await db.Templates
            .Where(t => archived ? t.ArchivedAt != null : t.ArchivedAt == null)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        var ids = templates.Select(t => t.Id).ToList();
        var currentIds = templates.Where(t => t.CurrentVersionId != null).Select(t => t.CurrentVersionId!.Value).ToList();
        var versions = await db.TemplateVersions
            .Where(v => ids.Contains(v.TemplateId) && (v.PublishedAt == null || currentIds.Contains(v.Id)))
            .ToListAsync(ct);

        return templates.Select(t =>
        {
            var current = versions.FirstOrDefault(v => v.Id == t.CurrentVersionId);
            var draft = versions.FirstOrDefault(v => v.TemplateId == t.Id && v.IsDraft);
            return new TemplateListItem(t.Id, t.Name, t.Description, current?.Version, draft is not null,
                (current ?? draft)?.Variables.Count ?? 0, t.ArchivedAt, t.UpdatedAt);
        }).ToList();
    }

    public async Task<Result<TemplateDetail>> GetAsync(Guid id, CancellationToken ct)
    {
        var template = await db.Templates.SingleOrDefaultAsync(t => t.Id == id, ct);
        return template is null ? NotFound : await DetailAsync(template, ct);
    }

    public async Task<Result<TemplateVersionDto>> GetVersionAsync(Guid id, Guid versionId, CancellationToken ct)
    {
        // O filtro por organização vale no template; a versão só é lida através dele.
        var version = await db.TemplateVersions
            .Where(v => v.Id == versionId && v.TemplateId == id && db.Templates.Any(t => t.Id == id))
            .SingleOrDefaultAsync(ct);
        return version is null ? Error.NotFound("Versão não encontrada.") : ToDto(version);
    }

    public async Task<Result<TemplateDetail>> CreateAsync(CreateTemplateRequest req, CancellationToken ct)
    {
        var errors = TemplateValidator.ValidateSave(new SaveTemplateRequest(req.Name, req.Description, req.BodyHtml, [], []));
        TemplateStarter? starter = null;
        if (!string.IsNullOrWhiteSpace(req.StarterId))
        {
            starter = TemplateStarters.Find(req.StarterId);
            if (starter is null)
                errors.Add("starterId", "Modelo pronto não encontrado.");
        }
        if (!errors.IsEmpty)
            return Error.Validation(errors);
        if (await meter.CheckNotOverdueAsync(tenant.RequireOrganizationId(), "criar templates", ct) is { } overdue)
            return overdue;
        if (await meter.CheckTemplateAsync(tenant.RequireOrganizationId(), ct) is { } limit)
            return limit;

        var userId = tenant.RequireUserId();
        var template = new Template(tenant.RequireOrganizationId(), req.Name!.Trim(), req.Description?.Trim() is { Length: > 0 } d ? d : null, userId);
        var draft = new TemplateVersion(template.Id, 1, userId, tenant.ExternalUserName);
        if (starter is not null)
            draft.Update(Clean(starter.BodyHtml), starter.Variables, starter.DefaultSignerRoles);
        else
            draft.Update(Clean(req.BodyHtml), [], []);

        // A versão precisa existir antes de o template apontar para ela (FK circular).
        db.TemplateVersions.Add(draft);
        db.Templates.Add(template);
        await db.SaveChangesAsync(ct);
        return await DetailAsync(template, ct);
    }

    public async Task<Result<TemplateDetail>> SaveDraftAsync(Guid id, SaveTemplateRequest req, uint? expectedRowVersion, CancellationToken ct)
    {
        var errors = TemplateValidator.ValidateSave(req);
        if (!errors.IsEmpty)
            return Error.Validation(errors);

        var template = await db.Templates.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
            return NotFound;
        if (template.ArchivedAt is not null)
            return Error.Conflict("Template arquivado. Desarquive para editar.", "archived");
        if (expectedRowVersion is { } expected)
            db.Entry(template).Property(t => t.RowVersion).OriginalValue = expected;

        var draft = await DraftAsync(template.Id, ct) ?? await NewDraftAsync(template.Id, ct);
        template.Rename(req.Name!, req.Description);
        draft.Update(
            Clean(req.BodyHtml),
            (req.Variables ?? []).Select(v => v with { Key = v.Key.Trim(), Label = v.Label.Trim() }),
            (req.DefaultSignerRoles ?? []).Select(r => r with { Role = r.Role.Trim() }));

        return await SaveAsync(template, ct);
    }

    public async Task<Result<TemplateDetail>> PublishAsync(Guid id, CancellationToken ct)
    {
        var template = await db.Templates.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
            return NotFound;
        if (template.ArchivedAt is not null)
            return Error.Conflict("Template arquivado. Desarquive para publicar.", "archived");

        var draft = await DraftAsync(template.Id, ct);
        if (draft is null)
            return Error.Conflict("Não há alterações para publicar.", "no_draft");

        var errors = TemplateValidator.ValidatePublish(draft.BodyHtml, draft.Variables);
        if (!errors.IsEmpty)
            return Error.Validation(errors, "Ainda falta algo para publicar.");

        template.Publish(draft);
        return await SaveAsync(template, ct);
    }

    public async Task<Result<TemplateDetail>> DiscardDraftAsync(Guid id, CancellationToken ct)
    {
        var template = await db.Templates.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
            return NotFound;
        if (template.CurrentVersionId is null)
            return Error.Conflict("O template ainda não tem versão publicada; não dá para descartar o único rascunho.", "no_published_version");

        var draft = await DraftAsync(template.Id, ct);
        if (draft is not null)
        {
            db.TemplateVersions.Remove(draft);
            template.Touch();
        }
        return await SaveAsync(template, ct);
    }

    public async Task<Result<TemplateDetail>> DuplicateAsync(Guid id, CancellationToken ct)
    {
        var source = await db.Templates.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (source is null)
            return NotFound;

        if (await meter.CheckNotOverdueAsync(source.OrganizationId, "criar templates", ct) is { } overdue)
            return overdue;
        if (await meter.CheckTemplateAsync(source.OrganizationId, ct) is { } limit)
            return limit;

        var content = await DraftAsync(source.Id, ct)
            ?? await db.TemplateVersions.SingleAsync(v => v.Id == source.CurrentVersionId, ct);

        var userId = tenant.RequireUserId();
        var name = $"Cópia de {source.Name}";
        if (name.Length > Template.NameMaxLength)
            name = name[..Template.NameMaxLength];

        var copy = new Template(source.OrganizationId, name, source.Description, userId);
        var draft = new TemplateVersion(copy.Id, 1, userId, tenant.ExternalUserName);
        draft.Update(content.BodyHtml, content.Variables, content.DefaultSignerRoles);
        db.TemplateVersions.Add(draft);
        db.Templates.Add(copy);
        await db.SaveChangesAsync(ct);
        return await DetailAsync(copy, ct);
    }

    public async Task<Result<TemplateDetail>> SetArchivedAsync(Guid id, bool archived, CancellationToken ct)
    {
        var template = await db.Templates.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
            return NotFound;
        if (archived)
            template.Archive();
        else if (template.ArchivedAt is not null)
        {
            if (await meter.CheckNotOverdueAsync(template.OrganizationId, "reativar templates", ct) is { } overdue)
                return overdue;
            if (await meter.CheckTemplateAsync(template.OrganizationId, ct) is { } limit)
                return limit;
            template.Unarchive();
        }
        return await SaveAsync(template, ct);
    }

    public async Task<Result<string>> PreviewAsync(Guid id, PreviewSource source, CancellationToken ct)
    {
        var template = await db.Templates.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
            return NotFound;

        var draft = await DraftAsync(template.Id, ct);
        var current = template.CurrentVersionId is { } cid ? await db.TemplateVersions.SingleAsync(v => v.Id == cid, ct) : null;
        var version = source == PreviewSource.Draft ? draft ?? current : current ?? draft;
        if (version is null || string.IsNullOrWhiteSpace(version.BodyHtml))
            return renderer.Render(template.Name, EmptyPreview, [], new Dictionary<string, string>());

        return renderer.RenderSample(template.Name, version.BodyHtml, version.Variables);
    }

    public Result<string> PreviewUnsaved(PreviewTemplateRequest req)
    {
        var body = req.BodyHtml ?? "";
        if (body.Length > TemplateValidator.BodyMaxLength)
            return Error.Validation("bodyHtml", "O texto do template está grande demais.");

        var name = string.IsNullOrWhiteSpace(req.Name) ? "Pré-visualização" : req.Name.Trim();
        if (string.IsNullOrWhiteSpace(body))
            return renderer.Render(name, EmptyPreview, [], new Dictionary<string, string>());

        // O editor manda o que está na tela, inclusive linhas incompletas: fica só o que dá para usar.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var variables = (req.Variables ?? [])
            .Take(TemplateValidator.MaxVariables)
            .Select(v => v with { Key = v.Key?.Trim() ?? "", Label = v.Label?.Trim() ?? "" })
            .Where(v => Placeholders.KeyPattern().IsMatch(v.Key) && Enum.IsDefined(v.Type) && seen.Add(v.Key))
            .Select(v => v.Label.Length == 0 ? v with { Label = v.Key } : v)
            .ToList();

        // O renderer sanitiza o HTML final, como no salvar.
        return renderer.RenderSample(name, body, variables);
    }

    private const string EmptyPreview = "<p style=\"color:#7b849a\">O texto do contrato aparece aqui.</p>";

    private async Task<Result<TemplateDetail>> SaveAsync(Template template, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Concurrency();
        }
        return await DetailAsync(template, ct);
    }

    private string Clean(string? html) => string.IsNullOrWhiteSpace(html) ? "" : sanitizer.Sanitize(html).Trim();

    private Task<TemplateVersion?> DraftAsync(Guid templateId, CancellationToken ct) =>
        db.TemplateVersions.SingleOrDefaultAsync(v => v.TemplateId == templateId && v.PublishedAt == null, ct);

    private async Task<TemplateVersion> NewDraftAsync(Guid templateId, CancellationToken ct)
    {
        var last = await db.TemplateVersions.Where(v => v.TemplateId == templateId).MaxAsync(v => (int?)v.Version, ct) ?? 0;
        var draft = new TemplateVersion(templateId, last + 1, tenant.RequireUserId(), tenant.ExternalUserName);
        db.TemplateVersions.Add(draft);
        return draft;
    }

    private async Task<TemplateDetail> DetailAsync(Template t, CancellationToken ct)
    {
        var versions = await db.TemplateVersions.Where(v => v.TemplateId == t.Id).OrderByDescending(v => v.Version).ToListAsync(ct);
        var creatorIds = versions.Select(v => v.CreatedBy).Distinct().ToList();
        var names = await db.Users.Where(u => creatorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var current = versions.FirstOrDefault(v => v.Id == t.CurrentVersionId);
        var draft = versions.FirstOrDefault(v => v.IsDraft);
        return new TemplateDetail(
            t.Id, t.Name, t.Description, t.ArchivedAt, t.UpdatedAt,
            current is null ? null : ToDto(current),
            draft is null ? null : ToDto(draft),
            versions.Select(v => new TemplateVersionSummary(v.Id, v.Version, v.PublishedAt,
                v.CreatedByExternal is { } external ? $"{external} (pela integração)" : names.GetValueOrDefault(v.CreatedBy))).ToList(),
            t.RowVersion);
    }

    private static TemplateVersionDto ToDto(TemplateVersion v) =>
        new(v.Id, v.Version, v.BodyHtml, v.Variables, v.DefaultSignerRoles, v.CreatedAt, v.PublishedAt);
}

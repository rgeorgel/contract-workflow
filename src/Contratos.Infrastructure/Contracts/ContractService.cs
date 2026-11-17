using System.Security.Cryptography;
using Contratos.Application.Common;
using Contratos.Application.Contracts;
using Contratos.Application.Signatures;
using Contratos.Application.Storage;
using Contratos.Application.Templates;
using Contratos.Domain.Contracts;
using Contratos.Domain.Templates;
using Contratos.Infrastructure.Billing;
using Contratos.Infrastructure.Messaging;
using Contratos.Infrastructure.Persistence;
using Contratos.Infrastructure.Signatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NpgsqlTypes;

namespace Contratos.Infrastructure.Contracts;

public sealed class ContractOptions
{
    public const string Section = "Contracts";
    /// <summary>Limite do PDF: 5 MB no plano gratuito da Autentique, 20 MB no profissional.</summary>
    public int MaxDocumentBytes { get; set; } = 20 * 1024 * 1024;
}

internal sealed class ContractService(
    AppDbContext db,
    ICurrentTenant tenant,
    DocumentRenderer renderer,
    IPdfRenderer pdf,
    IFileStorage storage,
    ISignatureProvider provider,
    TimeProvider clock,
    IOptions<ContractOptions> options,
    UsageMeter meter,
    ILogger<ContractService> logger) : IContractService
{
    private static readonly Error NotFound = Error.NotFound("Contrato não encontrado.");
    private static readonly Error CannotEdit = Error.Forbidden("Seu perfil não permite alterar contratos.");

    // ---------- Leitura ----------

    public async Task<Paged<ContractListItem>> ListAsync(ContractListQuery q, CancellationToken ct)
    {
        var page = Math.Max(1, q.Page);
        var pageSize = Math.Clamp(q.PageSize, 1, 100);
        var query = db.Contracts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Q))
            query = ApplySearch(query, q.Q.Trim());
        if (q.Status is { } status)
            query = query.Where(c => c.Status == status);
        if (q.TemplateId is { } templateId)
            query = query.Where(c => db.TemplateVersions.Any(v => v.Id == c.TemplateVersionId && v.TemplateId == templateId));
        if (q.CreatedBy is { } createdBy)
            query = query.Where(c => c.CreatedBy == createdBy);
        if (!string.IsNullOrWhiteSpace(q.ExternalReference))
        {
            var reference = q.ExternalReference.Trim();
            query = query.Where(c => c.ExternalReference == reference);
        }
        if (q.UpdatedFrom is { } updatedFrom)
            query = query.Where(c => c.UpdatedAt >= updatedFrom);

        var byCompletion = string.Equals(q.DateField, "completed", StringComparison.OrdinalIgnoreCase);
        if (q.From is { } from)
        {
            var start = StartOfDayBrt(from);
            query = byCompletion ? query.Where(c => c.CompletedAt >= start) : query.Where(c => c.CreatedAt >= start);
        }
        if (q.To is { } to)
        {
            var end = StartOfDayBrt(to.AddDays(1));
            query = byCompletion ? query.Where(c => c.CompletedAt < end) : query.Where(c => c.CreatedAt < end);
        }

        query = q.Sort switch
        {
            "created_asc" => query.OrderBy(c => c.CreatedAt),
            // Id como desempate: com a mesma hora, a paginação não pula nem repete contrato.
            "updated_asc" => query.OrderBy(c => c.UpdatedAt).ThenBy(c => c.Id),
            "title" => query.OrderBy(c => c.Title).ThenByDescending(c => c.CreatedAt),
            "number" => query.OrderByDescending(c => c.Number),
            "completed_desc" => query.OrderByDescending(c => c.CompletedAt).ThenByDescending(c => c.CreatedAt),
            _ => query.OrderByDescending(c => c.CreatedAt),
        };

        var total = await query.CountAsync(ct);
        var rows = await query
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new
            {
                c.Id, c.Number, c.Title, c.Status, c.CreatedAt, c.SentAt, c.CompletedAt, c.SendError, c.ExternalReference,
                c.UpdatedAt, c.ExpiresAt, HasSigned = c.SignedDocumentKey != null,
                // Sem IgnoreQueryFilters aqui: no EF ele desliga os filtros da consulta INTEIRA,
                // inclusive o de organização dos contratos.
                TemplateName = db.TemplateVersions.Where(v => v.Id == c.TemplateVersionId)
                    .Join(db.Templates, v => v.TemplateId, t => t.Id, (v, t) => t.Name).FirstOrDefault(),
                Total = c.Signers.Count,
                Signed = c.Signers.Count(s => s.Status == SignerStatus.Signed),
            })
            .ToListAsync(ct);

        var now = clock.GetUtcNow();
        var items = rows.Select(r => new ContractListItem(
            r.Id, r.Number, r.Title, r.Status, r.TemplateName ?? "—", r.Total, r.Signed, r.CreatedAt, r.SentAt, r.CompletedAt,
            r.SendError is not null,
            r.Status is ContractStatus.Sent or ContractStatus.PartiallySigned && r.SentAt is { } sent ? (int)(now - sent).TotalDays : null,
            r.ExternalReference, r.UpdatedAt, r.HasSigned, r.ExpiresAt))
            .ToList();
        return new Paged<ContractListItem>(items, page, pageSize, total);
    }

    public async Task<Result<ContractDetail>> GetAsync(Guid id, CancellationToken ct)
    {
        var contract = await LoadAsync(id, ct);
        return contract is null ? NotFound : await DetailAsync(contract, ct);
    }

    public async Task<Result<IReadOnlyList<ContractEventDto>>> GetEventsAsync(Guid id, CancellationToken ct)
    {
        var contract = await LoadAsync(id, ct);
        if (contract is null)
            return NotFound;

        var events = await db.ContractEvents.Where(e => e.ContractId == id)
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).ToListAsync(ct);
        var actorIds = events.Where(e => e.ActorUserId != null).Select(e => e.ActorUserId!.Value).Distinct().ToList();
        var actors = await db.Users.Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var signers = contract.Signers.ToDictionary(s => s.Id, s => s.Name);

        return events.Select(e => new ContractEventDto(
            e.Id, e.Type, e.OccurredAt,
            e.SignerId is { } sid ? signers.GetValueOrDefault(sid) ?? e.Payload.GetValueOrDefault("signerName") : null,
            e.ActorUserId is { } aid ? actors.GetValueOrDefault(aid) : null,
            e.Payload)).ToList();
    }

    public async Task<Result<FileResult>> GetDocumentAsync(Guid id, bool signed, CancellationToken ct)
    {
        var contract = await LoadAsync(id, ct);
        if (contract is null)
            return NotFound;
        var key = signed ? contract.SignedDocumentKey : contract.DocumentKey;
        if (key is null)
            return Error.NotFound(signed ? "O PDF assinado ainda não está disponível." : "O PDF é gerado no envio.");
        var stream = await storage.OpenReadAsync(key, ct);
        if (stream is null)
            return Error.NotFound("Arquivo não encontrado no storage.");
        var name = $"contrato-{contract.Number:0000}{(signed ? "-assinado" : "")}.pdf";
        return new FileResult(stream, name);
    }

    public async Task<Result<string>> PreviewAsync(Guid id, CancellationToken ct)
    {
        var contract = await LoadAsync(id, ct);
        if (contract is null)
            return NotFound;
        var version = await db.TemplateVersions.SingleAsync(v => v.Id == contract.TemplateVersionId, ct);
        return renderer.Render(contract.Title, version.BodyHtml, version.Variables, contract.VariableValues);
    }

    // ---------- Lote ----------

    public const int MaxBulkRows = 100;

    private sealed record BulkDraft(string Title, Dictionary<string, string> Values, DateTimeOffset? ExpiresAt,
        SigningMode Mode, string? Message, List<SignerData> Signers);

    public async Task<Result<BulkContractsResult>> CreateBulkAsync(BulkContractsRequest req, CancellationToken ct)
    {
        if (!tenant.CanEditContracts())
            return CannotEdit;
        var orgId = tenant.RequireOrganizationId();
        var org = await db.Organizations.SingleAsync(o => o.Id == orgId, ct);
        if (!org.Plan.BulkSend)
            return Error.Forbidden(
                $"O envio em lote faz parte dos planos {string.Join(" e ", Domain.Billing.Plans.All.Where(p => p.BulkSend).Select(p => p.Name))}. Mude de plano em Configurações.",
                "bulk_not_in_plan");
        if (!req.DryRun && org.IsPaymentOverdue)
            return UsageMeter.PaymentOverdue("criar contratos");

        var rows = req.Rows ?? [];
        if (rows.Count == 0)
            return Error.Validation("rows", "A planilha não tem nenhuma linha de contrato.");
        if (rows.Count > MaxBulkRows)
            return Error.Validation("rows", $"Envie no máximo {MaxBulkRows} contratos por vez.");

        var template = await db.Templates.SingleOrDefaultAsync(t => t.Id == req.TemplateId, ct);
        if (template is null)
            return Error.Validation("templateId", "Escolha um template.");
        if (template.ArchivedAt is not null || template.CurrentVersionId is null)
            return Error.Validation("templateId", "Este template não tem versão publicada ativa.");
        var version = await db.TemplateVersions.SingleAsync(v => v.Id == template.CurrentVersionId, ct);
        var today = ContractValidator.TodayInBrazil(clock.GetUtcNow());
        var userId = tenant.RequireUserId();

        var results = new List<BulkRowResult>();
        var drafts = new List<BulkDraft>();
        for (var i = 0; i < rows.Count; i++)
        {
            var (errors, draft) = ValidateRow(rows[i], version, org, userId, today);
            if (draft is not null)
                drafts.Add(draft);
            results.Add(new BulkRowResult(i + 1, null, null, errors.ToDictionary()));
        }

        var valid = results.Count(r => r.Errors.Count == 0);
        if (req.DryRun || valid < rows.Count)
            return new BulkContractsResult(rows.Count, valid, false, results);

        // Tudo certo: cria os rascunhos numa transação só (números sequenciais, sem buracos).
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var created = new List<Contract>();
        foreach (var d in drafts)
        {
            var contract = new Contract(orgId, await NextNumberAsync(orgId, ct), d.Title, version.Id, d.Message, userId);
            contract.UpdateDraft(d.Title, d.Values, d.ExpiresAt, d.Mode, d.Message);
            foreach (var signer in d.Signers)
                contract.AddSigner(signer);
            db.Contracts.Add(contract);
            db.ContractEvents.Add(new ContractEvent(contract.Id, ContractEventTypes.Created, actorUserId: userId,
                payload: new() { ["template"] = template.Name, ["bulk"] = "true" }));
            created.Add(contract);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        foreach (var c in created)
            await RefreshSearchAsync(c.Id, ct);

        return new BulkContractsResult(rows.Count, valid, true,
            results.Select((r, i) => r with { ContractId = created[i].Id, Number = created[i].Number }).ToList());
    }

    /// <summary>Confere uma linha inteira (dados, variáveis obrigatórias, signatários e canais do plano) sem gravar nada.</summary>
    private static (ValidationErrors Errors, BulkDraft? Draft) ValidateRow(
        BulkContractRow row, TemplateVersion version, Domain.Organizations.Organization org, Guid userId, DateOnly today)
    {
        var keys = version.Variables.Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
        var mode = row.SigningMode ?? SigningMode.Parallel;
        var errors = ContractValidator.ValidateDraft(
            new UpdateContractRequest(row.Title, row.VariableValues, row.ExpiresAt, mode, row.Message), version.Variables, today);
        var values = (row.VariableValues ?? [])
            .Where(kv => keys.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim(), StringComparer.Ordinal);
        foreach (var v in version.Variables.Where(v => v.Required && !values.ContainsKey(v.Key)))
            errors.Add($"variableValues.{v.Key}", $"Preencha “{v.Label}”.");

        // Signatários: validados um a um contra os anteriores da mesma linha (e-mail repetido etc.).
        var probe = new Contract(org.Id, 0, "lote", version.Id, null, userId);
        var signers = row.Signers ?? [];
        if (signers.Count == 0)
            errors.Add("signers", "Adicione ao menos um signatário.");
        if (signers.Count > Contract.MaxSigners)
            errors.Add("signers", $"Um contrato tem no máximo {Contract.MaxSigners} signatários.");
        var data = new List<SignerData>();
        for (var j = 0; j < signers.Count && j < Contract.MaxSigners; j++)
        {
            var s = signers[j];
            var signerErrors = ContractValidator.ValidateSigner(s, probe.Signers);
            var method = s.DeliveryMethod ?? DeliveryMethod.Email;
            if (Enum.IsDefined(method) && !org.Plan.Allows(method))
                signerErrors.Add("deliveryMethod", UsageMeter.ChannelNotInPlan(org.Plan, method));
            if (!signerErrors.IsEmpty)
            {
                foreach (var (field, messages) in signerErrors.ToDictionary())
                    foreach (var message in messages)
                        errors.Add($"signers[{j}].{field}", message);
                continue;
            }
            var d = ToData(s, s.SignOrder ?? j + 1);
            probe.AddSigner(d);
            data.Add(d);
        }

        if (!errors.IsEmpty)
            return (errors, null);
        var expiresAt = row.ExpiresAt is { } date
            ? new DateTimeOffset(date.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.FromHours(-3)).ToUniversalTime()
            : (DateTimeOffset?)null;
        return (errors, new BulkDraft(row.Title!.Trim(), values, expiresAt, mode,
            string.IsNullOrWhiteSpace(row.Message) ? org.Settings.DefaultMessage : row.Message, data));
    }

    // ---------- Contrato completo (API pública) ----------

    public async Task<Result<CreatedContract>> CreateCompleteAsync(CompleteContractRequest req, string? idempotencyKey, CancellationToken ct)
    {
        if (!tenant.CanEditContracts())
            return CannotEdit;
        var orgId = tenant.RequireOrganizationId();

        // Mesma chave de antes: devolve o que foi criado na primeira vez (a rede caiu, o cliente repetiu).
        if (idempotencyKey is not null
            && await db.Contracts.Include(c => c.Signers).SingleOrDefaultAsync(c => c.ApiIdempotencyKey == idempotencyKey, ct) is { } existing)
            return new CreatedContract(await DetailAsync(existing, ct), true);

        var org = await db.Organizations.SingleAsync(o => o.Id == orgId, ct);
        if (org.IsPaymentOverdue)
            return UsageMeter.PaymentOverdue("criar contratos");

        var template = req.TemplateId == Guid.Empty ? null : await db.Templates.SingleOrDefaultAsync(t => t.Id == req.TemplateId, ct);
        if (template is null)
            return Error.Validation("templateId", "Template não encontrado.");
        if (template.ArchivedAt is not null || template.CurrentVersionId is null)
            return Error.Validation("templateId", "Este template não tem versão publicada ativa.");
        var version = await db.TemplateVersions.SingleAsync(v => v.Id == template.CurrentVersionId, ct);

        var row = req.Row with { Title = string.IsNullOrWhiteSpace(req.Row.Title) ? template.Name : req.Row.Title };
        var userId = tenant.RequireUserId();
        var (errors, draft) = ValidateRow(row, version, org, userId, ContractValidator.TodayInBrazil(clock.GetUtcNow()));
        if (req.ExternalReference is { Length: > Contract.ExternalReferenceMaxLength })
            errors.Add("externalReference", $"Use no máximo {Contract.ExternalReferenceMaxLength} caracteres.");
        if (draft is null || !errors.IsEmpty)
            return Error.Validation(errors);

        // Numa transação: se a gravação falhar, o número do contrato não fica pulado.
        var tx = await db.Database.BeginTransactionAsync(ct);
        var contract = new Contract(orgId, await NextNumberAsync(orgId, ct), draft.Title, version.Id, draft.Message, userId);
        contract.UpdateDraft(draft.Title, draft.Values, draft.ExpiresAt, draft.Mode, draft.Message);
        foreach (var signer in draft.Signers)
            contract.AddSigner(signer);
        contract.SetIntegration(req.ExternalReference, idempotencyKey);
        db.Contracts.Add(contract);
        db.ContractEvents.Add(new ContractEvent(contract.Id, ContractEventTypes.Created, actorUserId: userId,
            payload: new() { ["template"] = template.Name }));
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException e) when (idempotencyKey is not null && e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Duas chamadas com a mesma chave ao mesmo tempo: a outra criou primeiro.
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var winner = await db.Contracts.Include(c => c.Signers).SingleAsync(c => c.ApiIdempotencyKey == idempotencyKey, ct);
            return new CreatedContract(await DetailAsync(winner, ct), true);
        }
        finally
        {
            await tx.DisposeAsync();
        }
        await RefreshSearchAsync(contract.Id, ct);
        var detail = await DetailAsync(contract, ct);
        // A busca regravou a linha (nova versão): quem enviar em seguida, na mesma requisição, carrega de novo.
        db.ChangeTracker.Clear();
        return new CreatedContract(detail, false);
    }

    // ---------- Rascunho ----------

    public async Task<Result<ContractDetail>> CreateAsync(CreateContractRequest req, CancellationToken ct)
    {
        if (!tenant.CanEditContracts())
            return CannotEdit;

        if (await meter.CheckNotOverdueAsync(tenant.RequireOrganizationId(), "criar contratos", ct) is { } overdue)
            return overdue;
        var template = await db.Templates.SingleOrDefaultAsync(t => t.Id == req.TemplateId, ct);
        if (template is null)
            return Error.Validation("templateId", "Escolha um template.");
        if (template.ArchivedAt is not null || template.CurrentVersionId is null)
            return Error.Validation("templateId", "Este template não tem versão publicada ativa.");

        var title = string.IsNullOrWhiteSpace(req.Title) ? template.Name : req.Title.Trim();
        if (title.Length > Contract.TitleMaxLength)
            return Error.Validation("title", $"Use no máximo {Contract.TitleMaxLength} caracteres.");

        var orgId = tenant.RequireOrganizationId();
        var org = await db.Organizations.SingleAsync(o => o.Id == orgId, ct);
        var number = await NextNumberAsync(orgId, ct);

        var contract = new Contract(orgId, number, title, template.CurrentVersionId.Value, org.Settings.DefaultMessage, tenant.RequireUserId());
        db.Contracts.Add(contract);
        db.ContractEvents.Add(new ContractEvent(contract.Id, ContractEventTypes.Created, actorUserId: tenant.UserId,
            payload: new() { ["template"] = template.Name }));
        await db.SaveChangesAsync(ct);
        await RefreshSearchAsync(contract.Id, ct);
        return await DetailAsync(contract, ct);
    }

    public async Task<Result<ContractDetail>> UpdateAsync(Guid id, UpdateContractRequest req, uint? expectedRowVersion, CancellationToken ct)
    {
        var (contract, error) = await LoadEditableDraftAsync(id, ct);
        if (contract is null)
            return error!;

        var version = await db.TemplateVersions.SingleAsync(v => v.Id == contract.TemplateVersionId, ct);
        var errors = ContractValidator.ValidateDraft(req, version.Variables, ContractValidator.TodayInBrazil(clock.GetUtcNow()));
        if (!errors.IsEmpty)
            return Error.Validation(errors);

        if (expectedRowVersion is { } expected)
            db.Entry(contract).Property(c => c.RowVersion).OriginalValue = expected;

        var keys = version.Variables.Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
        var values = (req.VariableValues ?? [])
            .Where(kv => keys.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim(), StringComparer.Ordinal);
        // Expira ao fim do dia escolhido (horário de Brasília).
        var expiresAt = req.ExpiresAt is { } date
            ? new DateTimeOffset(date.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.FromHours(-3)).ToUniversalTime()
            : (DateTimeOffset?)null;

        contract.UpdateDraft(req.Title!, values, expiresAt, req.SigningMode ?? contract.SigningMode, req.Message);
        return await SaveAndDetailAsync(contract, ct);
    }

    public async Task<Result<ContractDetail>> AddSignerAsync(Guid id, SignerRequest req, CancellationToken ct)
    {
        var (contract, error) = await LoadEditableDraftAsync(id, ct);
        if (contract is null)
            return error!;

        var errors = ContractValidator.ValidateSigner(req, contract.Signers);
        await CheckChannelAsync(req, errors, ct);
        if (!errors.IsEmpty)
            return Error.Validation(errors);
        if (contract.Signers.Count >= Contract.MaxSigners)
            return Error.Conflict($"Um contrato tem no máximo {Contract.MaxSigners} signatários.", "too_many_signers");

        var data = ToData(req, contract.Signers.Count == 0 ? 1 : contract.Signers.Max(s => s.SignOrder) + 1);
        var signer = contract.AddSigner(data);
        db.ContractSigners.Add(signer);
        db.ContractEvents.Add(new ContractEvent(contract.Id, ContractEventTypes.SignerAdded, signer.Id, tenant.UserId,
            new() { ["signerName"] = signer.Name }));
        return await SaveAndDetailAsync(contract, ct);
    }

    public async Task<Result<ContractDetail>> UpdateSignerAsync(Guid id, Guid signerId, SignerRequest req, CancellationToken ct)
    {
        var (contract, error) = await LoadEditableDraftAsync(id, ct);
        if (contract is null)
            return error!;
        var current = contract.Signers.SingleOrDefault(s => s.Id == signerId);
        if (current is null)
            return Error.NotFound("Signatário não encontrado.");

        var errors = ContractValidator.ValidateSigner(req, contract.Signers.Where(s => s.Id != signerId));
        if (req.DeliveryMethod != current.DeliveryMethod)
            await CheckChannelAsync(req, errors, ct);
        if (!errors.IsEmpty)
            return Error.Validation(errors);

        contract.UpdateSigner(signerId, ToData(req, current.SignOrder));
        return await SaveAndDetailAsync(contract, ct);
    }

    public async Task<Result<ContractDetail>> RemoveSignerAsync(Guid id, Guid signerId, CancellationToken ct)
    {
        var (contract, error) = await LoadEditableDraftAsync(id, ct);
        if (contract is null)
            return error!;
        if (contract.Signers.All(s => s.Id != signerId))
            return Error.NotFound("Signatário não encontrado.");

        var removed = contract.RemoveSigner(signerId);
        db.ContractSigners.Remove(removed);
        db.ContractEvents.Add(new ContractEvent(contract.Id, ContractEventTypes.SignerRemoved, null, tenant.UserId,
            new() { ["signerName"] = removed.Name }));
        return await SaveAndDetailAsync(contract, ct);
    }

    // ---------- Envio e ciclo de vida ----------

    public async Task<Result<ContractDetail>> SendAsync(Guid id, string? idempotencyKey, CancellationToken ct)
    {
        if (!tenant.CanEditContracts())
            return CannotEdit;
        var contract = await LoadAsync(id, ct);
        if (contract is null)
            return NotFound;

        if (!contract.IsDraft)
        {
            // Duplo clique com a mesma chave: responde como se fosse a primeira vez.
            if (idempotencyKey is not null && contract.SendIdempotencyKey == idempotencyKey)
                return await DetailAsync(contract, ct);
            return NotSendable(contract);
        }

        var version = await db.TemplateVersions.SingleAsync(v => v.Id == contract.TemplateVersionId, ct);
        var errors = ContractValidator.ValidateSend(contract, version.Variables, clock.GetUtcNow());
        if (!errors.IsEmpty)
            return Error.Validation(errors, "Ainda falta algo para enviar.");

        // Plano: canais (pode ter mudado depois do rascunho) e cota, antes de gerar o PDF.
        var org = await db.Organizations.SingleAsync(o => o.Id == contract.OrganizationId, ct);
        if (org.IsPaymentOverdue)
            return UsageMeter.PaymentOverdue("enviar contratos");
        var blocked = contract.Signers.Select(s => s.DeliveryMethod).Where(m => !org.Plan.Allows(m)).Distinct().ToList();
        if (blocked.Count > 0)
            return Error.Conflict(UsageMeter.ChannelNotInPlan(org.Plan, blocked[0]) + " Troque o canal dos signatários ou mude de plano.", "channel_not_in_plan");
        if (await meter.PrecheckSendAsync(org, ct) is { } noQuota)
            return noQuota;

        byte[] document;
        try
        {
            document = await pdf.RenderAsync(renderer.Render(contract.Title, version.BodyHtml, version.Variables, contract.VariableValues), ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(e, "Falha ao gerar o PDF do contrato {ContractId}", contract.Id);
            return Error.Conflict("Não foi possível gerar o PDF agora. Tente de novo em instantes.", "pdf_failed");
        }
        if (document.Length > options.Value.MaxDocumentBytes)
            return Error.Validation("document", $"O PDF passou do limite de {options.Value.MaxDocumentBytes / 1024 / 1024} MB.");

        var key = StorageKeys.Original(contract.OrganizationId, contract.Id);
        using (var stream = new MemoryStream(document))
            await storage.PutAsync(key, stream, "application/pdf", ct);
        var sha = Convert.ToHexStringLower(SHA256.HashData(document));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var consumed = await meter.ConsumeAsync(org, contract.Id, ct);
        if (!consumed.IsSuccess)
        {
            // Outro envio levou a última vaga entre a checagem e agora.
            await tx.RollbackAsync(ct);
            await storage.DeletePrefixAsync(key, ct);
            return consumed.Error!;
        }

        contract.MarkSent(key, sha, idempotencyKey);
        db.ContractEvents.Add(new ContractEvent(contract.Id, ContractEventTypes.Sent, actorUserId: tenant.UserId,
            payload: new()
            {
                ["sha256"] = sha,
                ["signers"] = contract.Signers.Count.ToString(),
                ["usage"] = consumed.Value == UsageSource.Credit ? "credit" : "plan",
            }));
        db.Enqueue(OutboxTypes.SendContract, new ContractMessage(contract.Id));

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outra requisição enviou primeiro (duplo clique sem chave, outra aba). A transação desfaz o consumo.
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var current = await LoadAsync(id, ct);
            return current is { IsDraft: false } && current.SendIdempotencyKey == idempotencyKey && idempotencyKey is not null
                ? await DetailAsync(current, ct)
                : NotSendable(current ?? contract);
        }
        return await DetailAsync(contract, ct);
    }

    /// <summary>Já saiu do rascunho: em andamento (já enviado) ou finalizado, inclusive cancelado sem nunca ter saído.</summary>
    private static Error NotSendable(Contract contract) => contract.IsFinal
        ? Error.Conflict("Este contrato já foi finalizado.", "already_final")
        : Error.Conflict("Este contrato já foi enviado.", "already_sent");

    public async Task<Result<ContractDetail>> ReturnToDraftAsync(Guid id, CancellationToken ct)
    {
        if (!tenant.CanEditContracts())
            return CannotEdit;
        var contract = await LoadAsync(id, ct);
        if (contract is null)
            return NotFound;
        var documentKey = contract.DocumentKey;
        if (!contract.ReturnToDraft())
            return Error.Conflict("Só um contrato com erro no envio, que ainda não chegou ao provedor, volta para rascunho.", "cannot_return_to_draft");

        if (documentKey is not null)
            await storage.DeletePrefixAsync(documentKey, ct);
        db.ContractEvents.Add(new ContractEvent(contract.Id, ContractEventTypes.ReturnedToDraft, actorUserId: tenant.UserId));
        // Não chegou ao provedor: o envio não conta e o crédito, se foi um, volta.
        return await SaveWithRefundAsync(contract, ct);
    }

    public async Task<Result<ContractDetail>> RetrySendAsync(Guid id, CancellationToken ct)
    {
        if (!tenant.CanEditContracts())
            return CannotEdit;
        var contract = await LoadAsync(id, ct);
        if (contract is null)
            return NotFound;
        if (contract.Status != ContractStatus.Sent || contract.AutentiqueDocumentId is not null || contract.SendError is null)
            return Error.Conflict("Não há envio com erro para repetir.", "nothing_to_retry");

        contract.ClearSendFailure();
        db.ContractEvents.Add(new ContractEvent(contract.Id, ContractEventTypes.SendRetried, actorUserId: tenant.UserId));
        db.Enqueue(OutboxTypes.SendContract, new ContractMessage(contract.Id));
        return await SaveAndDetailAsync(contract, ct);
    }

    public async Task<Result<ContractDetail>> CancelAsync(Guid id, CancellationToken ct)
    {
        if (!tenant.CanEditContracts())
            return CannotEdit;
        var contract = await LoadAsync(id, ct);
        if (contract is null)
            return NotFound;
        if (!contract.Cancel())
            return Error.Conflict("Este contrato já foi finalizado.", "already_final");

        db.ContractEvents.Add(new ContractEvent(contract.Id, ContractEventTypes.Cancelled, actorUserId: tenant.UserId));
        // A Autentique não tem "cancelar": o documento é removido lá, e o histórico fica aqui.
        if (contract.AutentiqueDocumentId is { } documentId)
        {
            db.Enqueue(OutboxTypes.DeleteProviderDocument, new ProviderDocumentMessage(documentId));
            return await SaveAndDetailAsync(contract, ct);
        }
        // Cancelado antes de chegar ao provedor (nada foi cobrado lá): devolve a cota ou o crédito.
        return await SaveWithRefundAsync(contract, ct);
    }

    public async Task<Result<ContractDetail>> ResendAsync(Guid id, Guid signerId, CancellationToken ct)
    {
        if (!tenant.CanEditContracts())
            return CannotEdit;
        var contract = await LoadAsync(id, ct);
        if (contract is null)
            return NotFound;
        var signer = contract.Signers.SingleOrDefault(s => s.Id == signerId);
        if (signer is null)
            return Error.NotFound("Signatário não encontrado.");
        if (!contract.IsOpen || !signer.CanResend)
            return Error.Conflict("Não dá para reenviar o link para este signatário agora.", "cannot_resend");

        db.ContractEvents.Add(new ContractEvent(contract.Id, ContractEventTypes.LinkResent, signer.Id, tenant.UserId,
            new() { ["channel"] = signer.DeliveryMethod.ToString() }));
        db.Enqueue(OutboxTypes.ResendSignature, new ResendMessage(contract.Id, signer.Id));
        return await SaveAndDetailAsync(contract, ct);
    }

    public async Task<Result> DeleteAsync(Guid id, int confirmNumber, bool alsoInProvider, CancellationToken ct)
    {
        var contract = await LoadAsync(id, ct);
        if (contract is null)
            return NotFound;
        if (!tenant.IsAdmin() && contract.CreatedBy != tenant.UserId)
            return Error.Forbidden("Só um Admin ou quem criou o contrato pode excluí-lo.");
        if (confirmNumber != contract.Number)
            return Error.Validation("confirmNumber", "Digite o número do contrato para confirmar.");

        await storage.DeletePrefixAsync(StorageKeys.ContractPrefix(contract.OrganizationId, contract.Id), ct);

        var deleteRemote = alsoInProvider && contract.AutentiqueDocumentId is not null;
        if (deleteRemote)
            db.Enqueue(OutboxTypes.DeleteProviderDocument, new ProviderDocumentMessage(contract.AutentiqueDocumentId!));
        db.DeletionLog.Add(new DeletionLogEntry
        {
            OrganizationId = contract.OrganizationId,
            ContractNumber = contract.Number,
            ContractTitle = contract.Title,
            DeletedBy = tenant.RequireUserId(),
            DeletedInAutentique = deleteRemote,
            SentAt = contract.SentAt,
        });
        db.Contracts.Remove(contract); // signatários e eventos vão em cascata
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            // Excluído antes de chegar ao provedor: o envio, se houve, não conta.
            if (contract.AutentiqueDocumentId is null)
                await meter.RefundAsync(contract.Id, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        logger.LogInformation("Contrato {ContractId} excluído", contract.Id);
        return Result.Ok();
    }

    // ---------- Busca ----------

    /// <summary>
    /// Número exato, texto completo (título, valores, signatários; com prefixo e sem acento)
    /// ou trecho de nome/e-mail de signatário (trigram).
    /// </summary>
    private IQueryable<Contract> ApplySearch(IQueryable<Contract> query, string term)
    {
        var number = int.TryParse(term.TrimStart('#').TrimStart('0'), out var n) ? n : -1;
        var like = $"%{EscapeLike(RemoveDiacritics(term))}%";
        var tsQuery = PrefixTsQuery(term);

        return tsQuery is null
            ? query.Where(c => c.Number == number
                || c.Signers.Any(s => EF.Functions.ILike(AppDbContext.ImmutableUnaccent(s.Name), like) || EF.Functions.ILike(s.Email!, like)))
            : query.Where(c => c.Number == number
                || EF.Property<NpgsqlTsVector>(c, Persistence.Configurations.ContractConfiguration.SearchVector)
                    .Matches(EF.Functions.ToTsQuery("pt_unaccent", tsQuery))
                || c.Signers.Any(s => EF.Functions.ILike(AppDbContext.ImmutableUnaccent(s.Name), like) || EF.Functions.ILike(s.Email!, like)));
    }

    /// <summary>"café aur" → "café:* &amp; aur:*". Só letras e números entram: nada de sintaxe de tsquery vinda do usuário.</summary>
    internal static string? PrefixTsQuery(string term)
    {
        var tokens = System.Text.RegularExpressions.Regex.Matches(term, @"[\p{L}\p{N}]+")
            .Select(m => m.Value).Where(t => t.Length > 0).Take(8).ToList();
        return tokens.Count == 0 ? null : string.Join(" & ", tokens.Select(t => $"{t}:*"));
    }

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string RemoveDiacritics(string s)
    {
        var normalized = s.Normalize(System.Text.NormalizationForm.FormD);
        return new string(normalized.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
            != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).Normalize(System.Text.NormalizationForm.FormC);
    }

    /// <summary>Meia-noite em Brasília, em UTC (o Npgsql só grava timestamptz com offset zero).</summary>
    private static DateTimeOffset StartOfDayBrt(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-3)).ToUniversalTime();

    /// <summary>search_vector é recalculado no banco, a partir do estado salvo de contrato e signatários.</summary>
    private Task RefreshSearchAsync(Guid contractId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT contracts_search_refresh({contractId})", ct);

    // ---------- Apoio ----------

    private Task<Contract?> LoadAsync(Guid id, CancellationToken ct) =>
        db.Contracts.Include(c => c.Signers).SingleOrDefaultAsync(c => c.Id == id, ct);

    private async Task<(Contract?, Error?)> LoadEditableDraftAsync(Guid id, CancellationToken ct)
    {
        if (!tenant.CanEditContracts())
            return (null, CannotEdit);
        var contract = await LoadAsync(id, ct);
        if (contract is null)
            return (null, NotFound);
        if (!contract.IsDraft)
            return (null, Error.Conflict("O contrato já foi enviado e não pode mais ser alterado.", "not_draft"));
        return (contract, null);
    }

    private async Task<Result<ContractDetail>> SaveAndDetailAsync(Contract contract, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Concurrency();
        }
        await RefreshSearchAsync(contract.Id, ct);
        return await DetailAsync(contract, ct);
    }

    private async Task<Result<ContractDetail>> SaveWithRefundAsync(Contract contract, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await meter.RefundAsync(contract.Id, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            return Error.Concurrency();
        }
        await RefreshSearchAsync(contract.Id, ct);
        return await DetailAsync(contract, ct);
    }

    private async Task CheckChannelAsync(SignerRequest req, ValidationErrors errors, CancellationToken ct)
    {
        var method = req.DeliveryMethod ?? DeliveryMethod.Email;
        var plan = await meter.PlanOfAsync(tenant.RequireOrganizationId(), ct);
        if (!plan.Allows(method))
            errors.Add("deliveryMethod", UsageMeter.ChannelNotInPlan(plan, method));
    }

    /// <summary>Número sequencial por organização, sem buracos por concorrência (UPDATE ... RETURNING trava a linha).</summary>
    private async Task<int> NextNumberAsync(Guid organizationId, CancellationToken ct)
    {
        var numbers = await db.Database
            .SqlQuery<int>($"UPDATE organizations SET contract_counter = contract_counter + 1 WHERE id = {organizationId} RETURNING contract_counter AS \"Value\"")
            .ToListAsync(ct);
        return numbers.Single();
    }

    private static SignerData ToData(SignerRequest req, int defaultOrder) => new(
        req.Name!.Trim(), req.Email, ContractValidator.NormalizePhone(req.Phone) is { Length: > 0 } p ? p : null,
        req.Cpf, req.DeliveryMethod ?? DeliveryMethod.Email, req.Role, req.Action ?? SignerAction.Sign, req.SignOrder ?? defaultOrder);

    private async Task<ContractDetail> DetailAsync(Contract c, CancellationToken ct)
    {
        var version = await db.TemplateVersions.SingleAsync(v => v.Id == c.TemplateVersionId, ct);
        var templateName = await db.Templates.Where(t => t.Id == version.TemplateId).Select(t => t.Name).SingleAsync(ct);
        var createdBy = await db.Users.Where(u => u.Id == c.CreatedBy).Select(u => u.Name).SingleOrDefaultAsync(ct);

        var canEdit = tenant.CanEditContracts();
        var permissions = new ContractPermissions(
            CanEdit: canEdit && c.IsDraft,
            CanSend: canEdit && c.IsDraft,
            CanCancel: canEdit && !c.IsFinal,
            CanDelete: tenant.IsAdmin() || c.CreatedBy == tenant.UserId,
            CanResend: canEdit && c.IsOpen,
            CanSimulate: canEdit && c.IsOpen && provider.Name == FakeSignatureProvider.ProviderName && c.AutentiqueDocumentId is not null,
            CanReturnToDraft: canEdit && c.CanReturnToDraft);

        return new ContractDetail(
            c.Id, c.Number, c.Title, c.Status, c.SigningMode, c.ExpiresAt, c.Message, c.VariableValues,
            new ContractTemplateInfo(version.TemplateId, templateName, version.Id, version.Version, version.Variables, version.DefaultSignerRoles),
            c.Signers.OrderBy(s => s.SignOrder).ThenBy(s => s.Id).Select(s => new SignerDto(
                s.Id, s.Name, s.Email, s.Phone, s.Cpf, s.DeliveryMethod, s.Role, s.Action, s.SignOrder, s.Status, s.ShortLink,
                s.ViewedAt, s.SignedAt, s.DeclinedAt, s.DeclineReason, c.IsOpen && s.CanResend)).ToList(),
            c.DocumentKey is not null, c.SignedDocumentKey is not null, c.DocumentSha256, c.SendError,
            createdBy, c.CreatedAt, c.UpdatedAt, c.SentAt, c.CompletedAt, c.CancelledAt, permissions, c.RowVersion, c.ExternalReference);
    }
}

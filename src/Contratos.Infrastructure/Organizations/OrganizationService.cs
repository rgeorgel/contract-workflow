using Contratos.Application.Common;
using Contratos.Application.Organizations;
using Contratos.Domain.Organizations;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Infrastructure.Organizations;

internal sealed class OrganizationService(AppDbContext db, ICurrentTenant tenant, TimeProvider clock) : IOrganizationService
{
    public async Task<OrganizationDto> GetAsync(CancellationToken ct)
    {
        var org = await db.Organizations.SingleAsync(o => o.Id == tenant.RequireOrganizationId(), ct);
        return await ToDtoAsync(org, ct);
    }

    public async Task<Result<OrganizationDto>> UpdateAsync(UpdateOrganizationRequest req, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var name = req.Name?.Trim() ?? "";
        if (name.Length < 2)
            errors.Add("name", "Informe o nome da empresa.");
        else if (name.Length > Organization.NameMaxLength)
            errors.Add("name", $"Use no máximo {Organization.NameMaxLength} caracteres.");

        var reminder = string.IsNullOrWhiteSpace(req.Reminder) ? null : req.Reminder;
        if (reminder is not null && !Organization.Reminders.Contains(reminder))
            errors.Add("reminder", "Lembrete inválido.");
        if ((req.DefaultMessage?.Length ?? 0) > Organization.MessageMaxLength)
            errors.Add("defaultMessage", $"Use no máximo {Organization.MessageMaxLength} caracteres.");
        var language = req.Language ?? "pt-BR";
        if (!Organization.Languages.Contains(language))
            errors.Add("language", "Idioma inválido.");
        if (req.StaleAlertDays is < 0 or > Organization.MaxAlertDays)
            errors.Add("staleAlertDays", $"Use de 0 a {Organization.MaxAlertDays} dias (0 desliga o aviso).");
        if (req.ExpiryAlertDays is < 0 or > Organization.MaxAlertDays)
            errors.Add("expiryAlertDays", $"Use de 0 a {Organization.MaxAlertDays} dias (0 desliga o aviso).");
        if (!errors.IsEmpty)
            return Error.Validation(errors);

        var org = await db.Organizations.SingleAsync(o => o.Id == tenant.RequireOrganizationId(), ct);
        org.Rename(name);
        org.UpdateSettings(reminder, req.DefaultMessage, language);
        org.UpdateAlerts(
            req.StaleAlertDays ?? org.Settings.StaleAlertDays,
            req.ExpiryAlertDays ?? org.Settings.ExpiryAlertDays,
            req.AutoResendBeforeExpiry ?? org.Settings.AutoResendBeforeExpiry);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(org, ct);
    }

    private async Task<OrganizationDto> ToDtoAsync(Organization org, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var month = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var sent = await CountSentAsync(org.Id, month, ct);
        return new OrganizationDto(org.Id, org.Name, org.Slug,
            new OrganizationSettingsDto(org.Settings.Reminder, org.Settings.DefaultMessage, org.Settings.Language,
                org.Settings.StaleAlertDays, org.Settings.ExpiryAlertDays, org.Settings.AutoResendBeforeExpiry),
            new UsageDto(month.ToString("yyyy-MM"), sent));
    }

    /// <summary>Contratos enviados no mês, pela view usage_by_month (inclui os já excluídos).</summary>
    private async Task<int> CountSentAsync(Guid organizationId, DateTimeOffset month, CancellationToken ct)
    {
        var counts = await db.Database
            .SqlQuery<int>($"SELECT contracts_sent::int AS \"Value\" FROM usage_by_month WHERE organization_id = {organizationId} AND month = {month}")
            .ToListAsync(ct);
        return counts.SingleOrDefault();
    }
}

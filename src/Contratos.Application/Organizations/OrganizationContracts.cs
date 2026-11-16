using Contratos.Application.Common;

namespace Contratos.Application.Organizations;

public sealed record OrganizationSettingsDto(
    string? Reminder, string? DefaultMessage, string Language, int StaleAlertDays, int ExpiryAlertDays, bool AutoResendBeforeExpiry);
public sealed record UsageDto(string Month, int ContractsSent);
public sealed record OrganizationDto(Guid Id, string Name, string Slug, OrganizationSettingsDto Settings, UsageDto Usage);
/// <param name="StaleAlertDays">Sem valor: mantém o atual. 0 desliga.</param>
public sealed record UpdateOrganizationRequest(
    string? Name, string? Reminder, string? DefaultMessage, string? Language,
    int? StaleAlertDays = null, int? ExpiryAlertDays = null, bool? AutoResendBeforeExpiry = null);

public interface IOrganizationService
{
    Task<OrganizationDto> GetAsync(CancellationToken ct);
    Task<Result<OrganizationDto>> UpdateAsync(UpdateOrganizationRequest request, CancellationToken ct);
}

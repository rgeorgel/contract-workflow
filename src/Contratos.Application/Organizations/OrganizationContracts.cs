using Contratos.Application.Common;

namespace Contratos.Application.Organizations;

public sealed record OrganizationSettingsDto(string? Reminder, string? DefaultMessage, string Language);
public sealed record UsageDto(string Month, int ContractsSent);
public sealed record OrganizationDto(Guid Id, string Name, string Slug, OrganizationSettingsDto Settings, UsageDto Usage);
public sealed record UpdateOrganizationRequest(string? Name, string? Reminder, string? DefaultMessage, string? Language);

public interface IOrganizationService
{
    Task<OrganizationDto> GetAsync(CancellationToken ct);
    Task<Result<OrganizationDto>> UpdateAsync(UpdateOrganizationRequest request, CancellationToken ct);
}

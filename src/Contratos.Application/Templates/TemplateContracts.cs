using Contratos.Application.Common;
using Contratos.Domain.Templates;

namespace Contratos.Application.Templates;

public sealed record TemplateListItem(
    Guid Id, string Name, string? Description, int? CurrentVersion, bool HasDraft,
    int VariableCount, DateTimeOffset? ArchivedAt, DateTimeOffset UpdatedAt);

public sealed record TemplateVersionDto(
    Guid Id, int Version, string BodyHtml, IReadOnlyList<TemplateVariable> Variables,
    IReadOnlyList<SignerRoleSuggestion> DefaultSignerRoles, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt);

public sealed record TemplateVersionSummary(Guid Id, int Version, DateTimeOffset? PublishedAt, string? CreatedByName);

public sealed record TemplateDetail(
    Guid Id, string Name, string? Description, DateTimeOffset? ArchivedAt, DateTimeOffset UpdatedAt,
    TemplateVersionDto? Current, TemplateVersionDto? Draft, IReadOnlyList<TemplateVersionSummary> Versions, uint RowVersion);

public sealed record CreateTemplateRequest(string? Name, string? Description, string? BodyHtml);

public sealed record SaveTemplateRequest(
    string? Name, string? Description, string? BodyHtml,
    List<TemplateVariable>? Variables, List<SignerRoleSuggestion>? DefaultSignerRoles);

public enum PreviewSource { Draft, Current }

public interface ITemplateService
{
    Task<IReadOnlyList<TemplateListItem>> ListAsync(bool archived, CancellationToken ct);
    Task<Result<TemplateDetail>> GetAsync(Guid id, CancellationToken ct);
    Task<Result<TemplateVersionDto>> GetVersionAsync(Guid id, Guid versionId, CancellationToken ct);
    Task<Result<TemplateDetail>> CreateAsync(CreateTemplateRequest request, CancellationToken ct);
    Task<Result<TemplateDetail>> SaveDraftAsync(Guid id, SaveTemplateRequest request, uint? expectedRowVersion, CancellationToken ct);
    Task<Result<TemplateDetail>> PublishAsync(Guid id, CancellationToken ct);
    Task<Result<TemplateDetail>> DiscardDraftAsync(Guid id, CancellationToken ct);
    Task<Result<TemplateDetail>> DuplicateAsync(Guid id, CancellationToken ct);
    Task<Result<TemplateDetail>> SetArchivedAsync(Guid id, bool archived, CancellationToken ct);
    Task<Result<string>> PreviewAsync(Guid id, PreviewSource source, CancellationToken ct);
}

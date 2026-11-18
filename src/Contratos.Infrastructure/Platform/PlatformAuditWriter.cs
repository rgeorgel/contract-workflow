using Contratos.Application.Platform;
using Contratos.Infrastructure.Persistence;

namespace Contratos.Infrastructure.Platform;

/// <summary>
/// Registra ações da equipe da plataforma na mesma transação da mudança (só adiciona; quem chama grava).
/// Quem fez: a conta logada no portal super admin ou, sem ela, a API de operação (chave da plataforma).
/// </summary>
public sealed class PlatformAuditWriter(AppDbContext db, ICurrentPlatformAdmin admin, TimeProvider clock)
{
    public const string ApiActor = "API da plataforma";

    public Task AddAsync(Guid organizationId, string action, string? details, CancellationToken ct)
    {
        var actorName = admin.Id is null ? ApiActor : ActorLabel(admin.Name ?? "?", admin.Email);
        Add(db, organizationId, admin.Id, actorName, action, details, clock.GetUtcNow());
        return Task.CompletedTask;
    }

    /// <summary>Com quem fez já conhecido (ex.: a conta da equipe por trás do "Entrar como").</summary>
    public static void Add(AppDbContext db, Guid organizationId, Guid? actorAdminId, string actorName, string action, string? details, DateTimeOffset at) =>
        db.PlatformAudit.Add(new PlatformAuditEntry
        {
            OrganizationId = organizationId,
            ActorAdminId = actorAdminId,
            ActorName = Truncate(actorName, 200),
            Action = action,
            Details = details is null ? null : Truncate(details, 500),
            CreatedAt = at,
        });

    /// <summary>"Nome &lt;email&gt;", como as ações da equipe aparecem na auditoria.</summary>
    public static string ActorLabel(string name, string? email) => $"{name} <{email}>";

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}

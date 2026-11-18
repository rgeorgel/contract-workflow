using Contratos.Application.Common;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Infrastructure.Platform;

/// <summary>
/// Registra ações da equipe da plataforma na mesma transação da mudança (só adiciona; quem chama grava).
/// Quem fez: a pessoa logada no portal ou, sem cookie, a API de operação (chave da plataforma).
/// </summary>
public sealed class PlatformAuditWriter(AppDbContext db, ICurrentTenant tenant, TimeProvider clock)
{
    public const string ApiActor = "API da plataforma";

    public async Task AddAsync(Guid organizationId, string action, string? details, CancellationToken ct)
    {
        string actorName = ApiActor;
        if (tenant.UserId is { } userId)
        {
            var user = await db.Users.AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new { u.Name, u.Email }).SingleOrDefaultAsync(ct);
            if (user is not null)
                actorName = ActorLabel(user.Name, user.Email);
        }
        Add(db, organizationId, tenant.UserId, actorName, action, details, clock.GetUtcNow());
    }

    /// <summary>Com quem fez já conhecido (ex.: a pessoa da equipe por trás do "Entrar como").</summary>
    public static void Add(AppDbContext db, Guid organizationId, Guid? actorUserId, string actorName, string action, string? details, DateTimeOffset at) =>
        db.PlatformAudit.Add(new PlatformAuditEntry
        {
            OrganizationId = organizationId,
            ActorUserId = actorUserId,
            ActorName = Truncate(actorName, 200),
            Action = action,
            Details = details is null ? null : Truncate(details, 500),
            CreatedAt = at,
        });

    /// <summary>"Nome &lt;email&gt;", como as ações da equipe aparecem na auditoria.</summary>
    public static string ActorLabel(string name, string? email) => $"{name} <{email}>";

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}

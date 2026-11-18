using Contratos.Application.Common;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Infrastructure.Platform;

/// <summary>
/// Registra ações da equipe da plataforma na mesma transação da mudança (só adiciona; quem chama grava).
/// Quem fez: a pessoa logada no portal ou, sem cookie, a API de operação (chave da plataforma).
/// </summary>
internal sealed class PlatformAuditWriter(AppDbContext db, ICurrentTenant tenant, TimeProvider clock)
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
                actorName = Truncate($"{user.Name} <{user.Email}>", 200);
        }
        db.PlatformAudit.Add(new PlatformAuditEntry
        {
            OrganizationId = organizationId,
            ActorUserId = tenant.UserId,
            ActorName = actorName,
            Action = action,
            Details = details is null ? null : Truncate(details, 500),
            CreatedAt = clock.GetUtcNow(),
        });
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}

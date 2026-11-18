using Contratos.Domain.Users;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Infrastructure.Integrations;

/// <summary>Quem a chave representa: a organização, quem criou a chave (dono dos contratos criados) e o nome dela.</summary>
public sealed record ApiKeyIdentity(Guid KeyId, string KeyName, Guid OrganizationId, Guid UserId, UserRole Role);

/// <summary>Acha a chave de uma requisição da API pública pelo hash.</summary>
public sealed class ApiKeyStore(AppDbContext db, TimeProvider clock)
{
    /// <summary>A API age como Editor: cria, envia e cancela contratos, mas não mexe em usuários nem em configurações.</summary>
    public const UserRole KeyRole = UserRole.Editor;

    public async Task<ApiKeyIdentity?> FindAsync(string token, CancellationToken ct)
    {
        if (!ApiKeyTokens.LooksValid(token))
            return null;
        var hash = ApiKeyTokens.Hash(token);
        var key = await db.ApiKeys.AsNoTracking()
            .Where(k => k.Hash == hash && k.RevokedAt == null)
            // Conta suspensa pela plataforma: a chave para de valer até a reativação.
            .Where(k => db.Organizations.Any(o => o.Id == k.OrganizationId && o.SuspendedAt == null))
            .Select(k => new { k.Id, k.Name, k.OrganizationId, k.CreatedBy, k.LastUsedAt })
            .SingleOrDefaultAsync(ct);
        if (key is null)
            return null;

        // "Último uso" com precisão de minuto: não grava a cada requisição.
        var now = clock.GetUtcNow();
        if (key.LastUsedAt is null || key.LastUsedAt < now.AddMinutes(-1))
            await db.ApiKeys.Where(k => k.Id == key.Id).ExecuteUpdateAsync(u => u.SetProperty(k => k.LastUsedAt, now), ct);

        return new ApiKeyIdentity(key.Id, key.Name, key.OrganizationId, key.CreatedBy, KeyRole);
    }
}

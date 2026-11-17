using System.Security.Cryptography;
using Contratos.Application.Common;
using Contratos.Application.Integrations;
using Contratos.Domain.Templates;
using Contratos.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Integrations;

/// <summary>Quem uma sessão de embed representa, a cada requisição da página no iframe.</summary>
public sealed record EmbedIdentity(
    Guid SessionId, Guid OrganizationId, Guid UserId, IReadOnlyList<string> Permissions, string? ExternalUserName);

/// <summary>
/// Sessões de embed: criação (API pública), abertura do link (troca do token de uso único pelo de acesso),
/// autenticação de cada requisição e os domínios que podem emoldurar a página.
/// </summary>
public sealed class EmbedSessions(
    AppDbContext db,
    ICurrentTenant tenant,
    TimeProvider clock,
    IOptions<AppOptions> app,
    IDataProtectionProvider protection,
    ILogger<EmbedSessions> logger)
{
    public const int DefaultMinutes = 60;
    public const int MinMinutes = 5;
    public const int MaxMinutes = 8 * 60;
    /// <summary>O link é para abrir agora: depois disso, o sistema do cliente cria outra sessão.</summary>
    public static readonly TimeSpan LaunchWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PreviewTicketLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Página do iframe: lista de templates ou o editor de um deles.</summary>
    public const string ListPage = "/embed/templates";
    public const string EditorPage = "/embed/template-editor";

    // ---------- Criação (API pública) ----------

    public async Task<Result<PublicEmbedSession>> CreateAsync(Guid apiKeyId, PublicCreateEmbedSessionRequest req, CancellationToken ct)
    {
        var orgId = tenant.RequireOrganizationId();
        var errors = new ValidationErrors();

        var permissions = req.Permissions is null
            ? EmbedPermissions.All.ToList()
            : req.Permissions.Select(p => p?.Trim().ToLowerInvariant() ?? "").Distinct().ToList();
        foreach (var p in permissions.Where(p => !EmbedPermissions.All.Contains(p)))
            errors.Add("permissions", $"Permissão desconhecida: {p}. Use edit, publish ou archive.");
        if (!permissions.Contains(EmbedPermissions.Edit) && permissions.Any(p => p is EmbedPermissions.Publish or EmbedPermissions.Archive))
            errors.Add("permissions", "publish e archive precisam de edit junto.");

        var externalName = req.User?.Name?.Trim();
        var externalId = req.User?.Id?.Trim();
        if (externalName is { Length: > TemplateVersion.ExternalAuthorMaxLength })
            errors.Add("user.name", $"Use no máximo {TemplateVersion.ExternalAuthorMaxLength} caracteres.");
        if (externalId is { Length: > EmbedSession.ExternalIdMaxLength })
            errors.Add("user.id", $"Use no máximo {EmbedSession.ExternalIdMaxLength} caracteres.");

        var minutes = req.ExpiresInMinutes ?? DefaultMinutes;
        if (minutes is < MinMinutes or > MaxMinutes)
            errors.Add("expiresInMinutes", $"Use de {MinMinutes} a {MaxMinutes} minutos.");

        var origins = await OrgOriginsAsync(orgId, ct);
        string? parentOrigin = null;
        if (!string.IsNullOrWhiteSpace(req.ParentOrigin))
        {
            var (normalized, error) = EmbedOrigins.Normalize(req.ParentOrigin, allowWildcard: false);
            if (error is not null)
                errors.Add("parentOrigin", error);
            else if (origins.Count > 0 && !origins.Any(o => EmbedOrigins.Matches(o, normalized!)))
                errors.Add("parentOrigin", "Este site não está entre os domínios do embed cadastrados em Integrações.");
            parentOrigin = normalized;
        }

        if (req.TemplateId is { } templateId && !await db.Templates.AnyAsync(t => t.Id == templateId, ct))
            errors.Add("templateId", "Template não encontrado.");

        if (!errors.IsEmpty)
            return Error.Validation(errors);
        if (origins.Count == 0)
            return Error.Conflict(
                "Cadastre o endereço do seu sistema em Integrações › Embed antes de criar sessões: só esses sites podem mostrar o embed.",
                "embed_origin_required");

        var now = clock.GetUtcNow();
        var (launch, launchHash) = EmbedTokens.Create(EmbedTokens.LaunchPrefix);
        var expires = now.AddMinutes(minutes);
        var session = new EmbedSession
        {
            OrganizationId = orgId,
            ApiKeyId = apiKeyId,
            UserId = tenant.RequireUserId(),
            Permissions = permissions,
            ExternalUserId = externalId is { Length: > 0 } ? externalId : null,
            ExternalUserName = externalName is { Length: > 0 } ? externalName : null,
            ParentOrigin = parentOrigin,
            LaunchTokenHash = launchHash,
            CreatedAt = now,
            LaunchExpiresAt = now + (LaunchWindow < expires - now ? LaunchWindow : expires - now),
            ExpiresAt = expires,
        };
        db.EmbedSessions.Add(session);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Sessão de embed {SessionId} criada pela chave {KeyId}", session.Id, apiKeyId);

        var page = req.TemplateId is { } id ? $"{EditorPage}?session={session.Id}&id={id}" : $"{ListPage}?session={session.Id}";
        // O token vai depois do #: não chega ao servidor nem aos logs de quem hospeda a página.
        var url = $"{app.Value.PublicBaseUrl.TrimEnd('/')}{page}#token={launch}";
        return ToPublic(session, url);
    }

    public async Task<Result> RevokeAsync(Guid id, CancellationToken ct)
    {
        var orgId = tenant.RequireOrganizationId();
        var rows = await db.EmbedSessions.Where(s => s.Id == id && s.OrganizationId == orgId && s.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, clock.GetUtcNow()), ct);
        if (rows == 0 && !await db.EmbedSessions.AnyAsync(s => s.Id == id && s.OrganizationId == orgId, ct))
            return Error.NotFound("Sessão não encontrada.");
        return Result.Ok();
    }

    // ---------- Página do iframe ----------

    /// <summary>
    /// Troca o token do link (uso único) por um token de acesso. Nulo se o link já foi aberto, expirou,
    /// foi revogado ou não existe (a página pede ao sistema do cliente para abrir de novo).
    /// </summary>
    public async Task<EmbedOpenResponse?> OpenAsync(EmbedOpenRequest req, CancellationToken ct)
    {
        if (req.SessionId is not { } id || req.Token is not { } token || !EmbedTokens.LooksValid(token, EmbedTokens.LaunchPrefix))
            return null;
        var launchHash = EmbedTokens.Hash(token);
        var (access, accessHash) = EmbedTokens.Create(EmbedTokens.AccessPrefix);
        var now = clock.GetUtcNow();

        // Uma única atualização condicional: duas abas abrindo o mesmo link, só uma ganha.
        var rows = await db.EmbedSessions
            .Where(s => s.Id == id && s.LaunchTokenHash == launchHash && s.OpenedAt == null && s.RevokedAt == null
                && s.LaunchExpiresAt > now && s.ExpiresAt > now
                && db.ApiKeys.Any(k => k.Id == s.ApiKeyId && k.RevokedAt == null))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.OpenedAt, now).SetProperty(s => s.AccessTokenHash, accessHash), ct);
        if (rows == 0)
            return null;

        var info = await InfoAsync(id, ct);
        return info is null ? null : new EmbedOpenResponse(access, info);
    }

    /// <summary>Sessão ativa do token de acesso (chave não revogada, dentro do prazo).</summary>
    public async Task<EmbedIdentity?> FindAsync(string accessToken, CancellationToken ct)
    {
        if (!EmbedTokens.LooksValid(accessToken, EmbedTokens.AccessPrefix))
            return null;
        var hash = EmbedTokens.Hash(accessToken);
        return await ActiveAsync(db.EmbedSessions.Where(s => s.AccessTokenHash == hash), ct);
    }

    public Task<EmbedIdentity?> FindByIdAsync(Guid sessionId, CancellationToken ct) =>
        ActiveAsync(db.EmbedSessions.Where(s => s.Id == sessionId && s.AccessTokenHash != null), ct);

    private async Task<EmbedIdentity?> ActiveAsync(IQueryable<EmbedSession> query, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var s = await query
            .Where(s => s.RevokedAt == null && s.ExpiresAt > now && db.ApiKeys.Any(k => k.Id == s.ApiKeyId && k.RevokedAt == null))
            .Select(s => new { s.Id, s.OrganizationId, s.UserId, s.Permissions, s.ExternalUserName })
            .SingleOrDefaultAsync(ct);
        return s is null ? null : new EmbedIdentity(s.Id, s.OrganizationId, s.UserId, s.Permissions, s.ExternalUserName);
    }

    public async Task<EmbedSessionInfo?> InfoAsync(Guid sessionId, CancellationToken ct)
    {
        var s = await db.EmbedSessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId, ct);
        if (s is null)
            return null;
        var orgName = await db.Organizations.Where(o => o.Id == s.OrganizationId).Select(o => o.Name).SingleAsync(ct);
        return new EmbedSessionInfo(s.Id, orgName, s.Permissions,
            s.ExternalUserName is null && s.ExternalUserId is null ? null : new PublicEmbedUser(s.ExternalUserId, s.ExternalUserName),
            await AllowedOriginsAsync(s, ct), s.ExpiresAt);
    }

    /// <summary>A página encerra a sessão (botão sair do sistema do cliente, por exemplo).</summary>
    public Task EndAsync(Guid sessionId, CancellationToken ct) =>
        db.EmbedSessions.Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, clock.GetUtcNow()), ct);

    /// <summary>
    /// Quem pode emoldurar a página desta sessão. Vale também para sessão expirada ou revogada (para a página
    /// conseguir mostrar o aviso dentro do sistema do cliente); sessão que não existe não abre em lugar nenhum.
    /// </summary>
    public async Task<IReadOnlyList<string>> FrameAncestorsAsync(Guid sessionId, CancellationToken ct)
    {
        var s = await db.EmbedSessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId, ct);
        return s is null ? [] : await AllowedOriginsAsync(s, ct);
    }

    private async Task<IReadOnlyList<string>> AllowedOriginsAsync(EmbedSession s, CancellationToken ct)
    {
        var origins = await OrgOriginsAsync(s.OrganizationId, ct);
        // O domínio pode ter sido removido depois de criar a sessão: aí ela não abre mais.
        if (s.ParentOrigin is { } parent)
            return origins.Any(o => EmbedOrigins.Matches(o, parent)) ? [parent] : [];
        return origins;
    }

    private async Task<List<string>> OrgOriginsAsync(Guid orgId, CancellationToken ct) =>
        await db.EmbedOrigins.Where(o => o.OrganizationId == orgId).OrderBy(o => o.CreatedAt).Select(o => o.Origin).ToListAsync(ct);

    // ---------- Pré-visualização ----------
    // O iframe da pré-visualização é carregado pelo endereço (src) e não manda o cabeçalho Authorization:
    // a página pede um link com um ticket curto, que só abre a pré-visualização daquele template.

    private ITimeLimitedDataProtector Tickets => protection.CreateProtector("Contratos.Embed.Preview.v1").ToTimeLimitedDataProtector();

    public string PreviewTicket(Guid sessionId, Guid templateId) =>
        Tickets.Protect($"{sessionId:N}.{templateId:N}", PreviewTicketLifetime);

    public (Guid SessionId, Guid TemplateId)? ReadPreviewTicket(string ticket)
    {
        try
        {
            var parts = Tickets.Unprotect(ticket).Split('.');
            return parts.Length == 2 && Guid.TryParse(parts[0], out var s) && Guid.TryParse(parts[1], out var t) ? (s, t) : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static PublicEmbedSession ToPublic(EmbedSession s, string url) => new(
        s.Id, url, s.Permissions,
        s.ExternalUserName is null && s.ExternalUserId is null ? null : new PublicEmbedUser(s.ExternalUserId, s.ExternalUserName),
        s.ParentOrigin, s.LaunchExpiresAt, s.ExpiresAt);
}

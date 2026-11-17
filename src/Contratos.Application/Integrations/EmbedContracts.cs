namespace Contratos.Application.Integrations;

// ---------- Domínios do embed (tela de Integrações) ----------

public sealed record EmbedOriginDto(Guid Id, string Origin, string? CreatedByName, DateTimeOffset CreatedAt);

public sealed record EmbedOriginRequest(string? Origin);

// ---------- API pública: sessões de embed ----------

/// <param name="Id">Id da pessoa no sistema do cliente (só para referência).</param>
/// <param name="Name">Nome que aparece no histórico de versões dos templates.</param>
public sealed record PublicEmbedUser(string? Id, string? Name);

/// <param name="Permissions">edit, publish, archive. Omitido: todas. Lista vazia: só leitura.</param>
/// <param name="ParentOrigin">Site exato que vai abrir o iframe (precisa estar entre os domínios cadastrados).</param>
/// <param name="TemplateId">Abre direto no editor deste template.</param>
public sealed record PublicCreateEmbedSessionRequest(
    List<string>? Permissions, PublicEmbedUser? User, string? ParentOrigin, int? ExpiresInMinutes, Guid? TemplateId);

/// <param name="Url">Endereço do iframe. Abre uma vez só: cada abertura precisa de uma sessão nova.</param>
/// <param name="LaunchExpiresAt">Até quando o link pode ser aberto.</param>
public sealed record PublicEmbedSession(
    Guid Id, string Url, IReadOnlyList<string> Permissions, PublicEmbedUser? User, string? ParentOrigin,
    DateTimeOffset LaunchExpiresAt, DateTimeOffset ExpiresAt);

// ---------- API do embed (usada pela página dentro do iframe) ----------

public sealed record EmbedOpenRequest(Guid? SessionId, string? Token);

/// <param name="AllowedOrigins">Sites que podem receber os avisos (postMessage) da página.</param>
public sealed record EmbedSessionInfo(
    Guid Id, string OrganizationName, IReadOnlyList<string> Permissions, PublicEmbedUser? User,
    IReadOnlyList<string> AllowedOrigins, DateTimeOffset ExpiresAt);

public sealed record EmbedOpenResponse(string AccessToken, EmbedSessionInfo Session);

public sealed record EmbedPreviewLink(string Url);

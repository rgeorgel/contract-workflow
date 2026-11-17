namespace Contratos.Infrastructure.Integrations;

/// <summary>Site do cliente que pode mostrar o embed num iframe (vira o <c>frame-ancestors</c> da página).</summary>
public sealed class EmbedOrigin
{
    public const int MaxLength = 200;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    /// <summary>Normalizada: <c>https://crm.exemplo.com.br</c>, <c>https://*.exemplo.com.br</c> ou <c>http://localhost:3000</c>.</summary>
    public string Origin { get; set; } = null!;
    public Guid CreatedBy { get; set; }
    /// <summary>Chave da API pública que cadastrou (nulo: cadastrado pela tela).</summary>
    public Guid? CreatedByApiKeyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>O que uma sessão de embed pode fazer além de ver os templates.</summary>
public static class EmbedPermissions
{
    /// <summary>Criar, editar, duplicar e descartar rascunho.</summary>
    public const string Edit = "edit";
    public const string Publish = "publish";
    /// <summary>Arquivar e desarquivar.</summary>
    public const string Archive = "archive";

    public static IReadOnlyList<string> All { get; } = [Edit, Publish, Archive];
}

/// <summary>
/// Sessão de embed criada pelo servidor do cliente (com a chave de API) para mostrar a tela de templates
/// dentro do sistema dele. O link traz um token de uso único; a página troca esse token por um token de acesso,
/// que só vale na API do embed (<c>/api/embed/v1</c>) e só até <see cref="ExpiresAt"/>.
/// Só os hashes dos dois tokens ficam gravados.
/// </summary>
public sealed class EmbedSession
{
    public const int ExternalIdMaxLength = 100;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public Guid ApiKeyId { get; set; }
    /// <summary>Quem criou a chave: dono dos templates criados pelo embed, como na API.</summary>
    public Guid UserId { get; set; }
    public List<string> Permissions { get; set; } = [];
    public string? ExternalUserId { get; set; }
    public string? ExternalUserName { get; set; }
    /// <summary>Site exato que vai abrir o iframe; vazio vale para todos os domínios cadastrados.</summary>
    public string? ParentOrigin { get; set; }
    public byte[] LaunchTokenHash { get; set; } = null!;
    public byte[]? AccessTokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Até quando o link pode ser aberto pela primeira vez.</summary>
    public DateTimeOffset LaunchExpiresAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool Can(string permission) => Permissions.Contains(permission);
}

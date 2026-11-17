namespace Contratos.Infrastructure.Integrations;

/// <summary>
/// Chave da API pública. Só o SHA-256 fica gravado: a chave inteira aparece uma vez, na criação.
/// Age em nome de quem a criou (contratos ficam com essa pessoa como criadora), com perfil de Editor.
/// </summary>
public sealed class ApiKey
{
    public const int NameMaxLength = 80;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = null!;
    /// <summary>Começo da chave (ex.: <c>ctr_Ab12Cd34</c>), para reconhecer na lista.</summary>
    public string Prefix { get; set; } = null!;
    public byte[] Hash { get; set; } = null!;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>Endereço do cliente que recebe os eventos. O segredo de assinatura fica cifrado (Data Protection).</summary>
public sealed class WebhookEndpoint
{
    public const int UrlMaxLength = 500;
    public const int DescriptionMaxLength = 200;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public string Url { get; set; } = null!;
    public string? Description { get; set; }
    /// <summary>Tipos assinados; vazio recebe todos.</summary>
    public List<string> Events { get; set; } = [];
    public string SecretProtected { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public Guid CreatedBy { get; set; }
    /// <summary>Chave da API pública que cadastrou (nulo: cadastrado pela tela). Revogar a chave não apaga o webhook.</summary>
    public Guid? CreatedByApiKeyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastFailureAt { get; set; }
    /// <summary>Entregas seguidas que esgotaram as tentativas; volta a zero na primeira que der certo.</summary>
    public int FailureStreak { get; set; }

    public bool Subscribes(string type) => Events.Count == 0 || Events.Contains(type);
}

/// <summary>Evento gerado uma vez, com o corpo já pronto (texto, para a assinatura bater byte a byte).</summary>
public sealed class WebhookEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public string Type { get; set; } = null!;
    public Guid? ContractId { get; set; }
    public string Payload { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class WebhookDeliveryStatus
{
    public const string Pending = "pending";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}

/// <summary>Uma entrega de um evento para um endpoint, com as tentativas.</summary>
public sealed class WebhookDelivery
{
    public long Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid EventId { get; set; }
    public Guid EndpointId { get; set; }
    public string Status { get; set; } = WebhookDeliveryStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastAttemptAt { get; set; }
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }
    /// <summary>Começo da resposta do cliente, para depurar pela tela.</summary>
    public string? ResponseBody { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

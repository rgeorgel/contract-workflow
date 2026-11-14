namespace Contratos.Infrastructure.Messaging;

/// <summary>Chamada a um serviço externo gravada na mesma transação da mudança de estado; o worker executa com retentativa.</summary>
public sealed class OutboxMessage
{
    public long Id { get; set; }
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = "{}";
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
}

/// <summary>Webhook recebido, guardado antes de processar. O índice único em (provider, external_event_id) dá a idempotência.</summary>
public sealed class WebhookInboxItem
{
    public long Id { get; set; }
    public string Provider { get; set; } = null!;
    public string ExternalEventId { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Rastro mínimo de um contrato excluído, sem o conteúdo.</summary>
public sealed class DeletionLogEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public int ContractNumber { get; set; }
    public string ContractTitle { get; set; } = null!;
    public Guid DeletedBy { get; set; }
    public DateTimeOffset DeletedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool DeletedInAutentique { get; set; }
    /// <summary>Mantém o envio na contagem de uso do mês mesmo depois da exclusão.</summary>
    public DateTimeOffset? SentAt { get; set; }
}

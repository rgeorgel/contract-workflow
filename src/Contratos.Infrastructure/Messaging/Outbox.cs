using System.Text.Json;
using Contratos.Infrastructure.Persistence;

namespace Contratos.Infrastructure.Messaging;

public static class OutboxTypes
{
    public const string SendContract = "SendContract";
    public const string DeleteProviderDocument = "DeleteProviderDocument";
    public const string ResendSignature = "ResendSignature";
    public const string StoreSignedDocument = "StoreSignedDocument";
}

public sealed record ContractMessage(Guid ContractId);
public sealed record ProviderDocumentMessage(string DocumentId);
public sealed record ResendMessage(Guid ContractId, Guid SignerId);

internal static class OutboxExtensions
{
    /// <summary>Grava a mensagem no mesmo SaveChanges da mudança que a originou.</summary>
    public static void Enqueue<T>(this AppDbContext db, string type, T payload) =>
        db.OutboxMessages.Add(new OutboxMessage { Type = type, Payload = JsonSerializer.Serialize(payload, JsonColumn.Options) });

    public static T Read<T>(this OutboxMessage message) =>
        JsonSerializer.Deserialize<T>(message.Payload, JsonColumn.Options)!;
}

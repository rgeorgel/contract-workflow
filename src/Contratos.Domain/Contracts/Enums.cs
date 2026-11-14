namespace Contratos.Domain.Contracts;

public enum ContractStatus { Draft, Sent, PartiallySigned, Completed, Declined, Expired, Cancelled }
public enum SigningMode { Parallel, Sequential }
public enum DeliveryMethod { Email, WhatsApp, Sms }
public enum SignerStatus { Pending, Viewed, Signed, Declined, DeliveryFailed }

/// <summary>Tipos de evento da linha do tempo (contract_events.type).</summary>
public static class ContractEventTypes
{
    public const string Created = "Created";
    public const string SignerAdded = "SignerAdded";
    public const string SignerRemoved = "SignerRemoved";
    public const string Sent = "ContractSent";
    public const string SentToProvider = "SentToProvider";
    public const string SendFailed = "SendFailed";
    public const string SendRetried = "SendRetried";
    public const string SignerViewed = "SignerViewed";
    public const string SignerSigned = "SignerSigned";
    public const string SignerDeclined = "SignerDeclined";
    public const string DeliveryFailed = "DeliveryFailed";
    public const string LinkResent = "LinkResent";
    public const string Completed = "Completed";
    public const string SignedDocumentStored = "SignedDocumentStored";
    public const string Cancelled = "Cancelled";
    public const string Expired = "Expired";
    public const string ProviderDocumentDeleted = "ProviderDocumentDeleted";
}

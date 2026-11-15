using Contratos.Domain.Templates;

namespace Contratos.Domain.Contracts;

public sealed class Contract
{
    public const int TitleMaxLength = 200;
    public const int MessageMaxLength = 500;
    public const int MaxSigners = 30;

    private readonly List<ContractSigner> _signers = [];

    private Contract() { } // EF Core

    public Contract(Guid organizationId, int number, string title, Guid templateVersionId, string? message, Guid createdBy)
    {
        Id = Guid.CreateVersion7();
        OrganizationId = organizationId;
        Number = number;
        Title = title.Trim();
        TemplateVersionId = templateVersionId;
        Message = message;
        CreatedBy = createdBy;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public int Number { get; private set; }
    public string Title { get; private set; } = null!;
    public Guid TemplateVersionId { get; private set; }
    public Dictionary<string, string> VariableValues { get; private set; } = [];
    public ContractStatus Status { get; private set; }
    public SigningMode SigningMode { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public string? Message { get; private set; }

    public string? DocumentKey { get; private set; }
    public string? DocumentSha256 { get; private set; }
    public string? SignedDocumentKey { get; private set; }
    public string? AutentiqueDocumentId { get; private set; }

    /// <summary>Última falha ao entregar ao provedor, depois de esgotar as tentativas ("Erro no envio").</summary>
    public string? SendError { get; private set; }
    public string? SendIdempotencyKey { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }
    /// <summary>Última consulta do documento no provedor (reconciliação de webhooks perdidos).</summary>
    public DateTimeOffset? ProviderCheckedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint RowVersion { get; private set; }

    public IReadOnlyList<ContractSigner> Signers => _signers;

    public bool IsDraft => Status == ContractStatus.Draft;
    public bool IsOpen => Status is ContractStatus.Sent or ContractStatus.PartiallySigned;
    public bool IsFinal => Status is ContractStatus.Completed or ContractStatus.Declined or ContractStatus.Expired or ContractStatus.Cancelled;

    // ---------- Rascunho ----------

    public void UpdateDraft(string title, IReadOnlyDictionary<string, string> values, DateTimeOffset? expiresAt, SigningMode mode, string? message)
    {
        EnsureDraft();
        Title = title.Trim();
        VariableValues = values.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        ExpiresAt = expiresAt;
        SigningMode = mode;
        Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        Touch();
    }

    public ContractSigner AddSigner(SignerData data)
    {
        EnsureDraft();
        if (_signers.Count >= MaxSigners)
            throw new DomainException($"Um contrato tem no máximo {MaxSigners} signatários.");
        var signer = new ContractSigner(Id, data);
        _signers.Add(signer);
        Touch();
        return signer;
    }

    public void UpdateSigner(Guid signerId, SignerData data)
    {
        EnsureDraft();
        FindSigner(signerId).Update(data);
        Touch();
    }

    public ContractSigner RemoveSigner(Guid signerId)
    {
        EnsureDraft();
        var signer = FindSigner(signerId);
        _signers.Remove(signer);
        Touch();
        return signer;
    }

    // ---------- Envio ----------

    public void MarkSent(string documentKey, string sha256, string? idempotencyKey)
    {
        EnsureDraft();
        if (_signers.Count == 0)
            throw new DomainException("Adicione ao menos um signatário antes de enviar.");
        DocumentKey = documentKey;
        DocumentSha256 = sha256;
        SendIdempotencyKey = idempotencyKey;
        Status = ContractStatus.Sent;
        SentAt = DateTimeOffset.UtcNow;
        Touch();
    }

    /// <summary>O provedor aceitou o documento. <paramref name="signatures"/> vem na ordem de <see cref="SignersInSendOrder"/>.</summary>
    public void AttachProviderDocument(string documentId, IReadOnlyList<(string PublicId, string? ShortLink)> signatures)
    {
        var ordered = SignersInSendOrder();
        if (signatures.Count != ordered.Count)
            throw new DomainException("O provedor devolveu um número diferente de assinaturas.");
        AutentiqueDocumentId = documentId;
        SendError = null;
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].AttachProvider(signatures[i].PublicId, signatures[i].ShortLink);
        Touch();
    }

    /// <summary>Marca a consulta ao provedor. Não mexe em UpdatedAt: não é uma mudança do contrato.</summary>
    public void MarkProviderChecked(DateTimeOffset at) => ProviderCheckedAt = at;

    public void RecordSendFailure(string error)
    {
        SendError = error.Length > 500 ? error[..500] : error;
        Touch();
    }

    public void ClearSendFailure()
    {
        SendError = null;
        Touch();
    }

    public IReadOnlyList<ContractSigner> SignersInSendOrder() =>
        _signers.OrderBy(s => s.SignOrder).ThenBy(s => s.Id).ToList();

    // ---------- Eventos do provedor ----------

    public bool SignerViewed(ContractSigner signer, DateTimeOffset at) => IsOpen && signer.MarkViewed(at) && Touched();

    public bool SignerSigned(ContractSigner signer, DateTimeOffset at)
    {
        if (!IsOpen || !signer.MarkSigned(at))
            return false;
        Status = ContractStatus.PartiallySigned;
        return Touched();
    }

    public bool SignerDeclined(ContractSigner signer, DateTimeOffset at, string? reason)
    {
        if (!IsOpen || !signer.MarkDeclined(at, reason))
            return false;
        Status = ContractStatus.Declined;
        return Touched();
    }

    public bool SignerDeliveryFailed(ContractSigner signer) => IsOpen && signer.MarkDeliveryFailed() && Touched();

    public bool Complete(DateTimeOffset at)
    {
        if (!IsOpen)
            return false;
        foreach (var s in _signers.Where(s => s.Status != SignerStatus.Signed))
            s.MarkSigned(at);
        Status = ContractStatus.Completed;
        CompletedAt = at;
        return Touched();
    }

    public void AttachSignedDocument(string key)
    {
        SignedDocumentKey = key;
        Touch();
    }

    public bool Cancel()
    {
        if (IsFinal)
            return false;
        Status = ContractStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        return Touched();
    }

    public bool Expire(DateTimeOffset now)
    {
        if (!IsOpen || ExpiresAt is null || ExpiresAt > now)
            return false;
        Status = ContractStatus.Expired;
        return Touched();
    }

    public ContractSigner FindSigner(Guid signerId) =>
        _signers.SingleOrDefault(s => s.Id == signerId) ?? throw new DomainException("Signatário não encontrado.");

    private void EnsureDraft()
    {
        if (!IsDraft)
            throw new DomainException("Só é possível alterar o contrato enquanto ele é rascunho.");
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
    private bool Touched() { Touch(); return true; }
}

public sealed record SignerData(
    string Name, string? Email, string? Phone, string? Cpf, DeliveryMethod DeliveryMethod,
    string? Role, SignerAction Action, int SignOrder);

public sealed class ContractSigner
{
    private ContractSigner() { } // EF Core

    internal ContractSigner(Guid contractId, SignerData data)
    {
        Id = Guid.CreateVersion7();
        ContractId = contractId;
        Update(data);
    }

    public Guid Id { get; private set; }
    public Guid ContractId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Cpf { get; private set; }
    public DeliveryMethod DeliveryMethod { get; private set; }
    public string? Role { get; private set; }
    public SignerAction Action { get; private set; }
    public int SignOrder { get; private set; }
    public SignerStatus Status { get; private set; }
    public string? AutentiquePublicId { get; private set; }
    public string? ShortLink { get; private set; }
    public DateTimeOffset? ViewedAt { get; private set; }
    public DateTimeOffset? SignedAt { get; private set; }
    public DateTimeOffset? DeclinedAt { get; private set; }
    public string? DeclineReason { get; private set; }

    internal void Update(SignerData d)
    {
        Name = d.Name.Trim();
        Email = string.IsNullOrWhiteSpace(d.Email) ? null : d.Email.Trim();
        Phone = string.IsNullOrWhiteSpace(d.Phone) ? null : d.Phone.Trim();
        Cpf = string.IsNullOrWhiteSpace(d.Cpf) ? null : Documents.Digits(d.Cpf);
        DeliveryMethod = d.DeliveryMethod;
        Role = string.IsNullOrWhiteSpace(d.Role) ? null : d.Role.Trim();
        Action = d.Action;
        SignOrder = d.SignOrder;
    }

    internal void AttachProvider(string publicId, string? shortLink)
    {
        AutentiquePublicId = publicId;
        ShortLink = shortLink;
    }

    internal bool MarkViewed(DateTimeOffset at)
    {
        if (Status is not (SignerStatus.Pending or SignerStatus.DeliveryFailed))
            return false;
        Status = SignerStatus.Viewed;
        ViewedAt ??= at;
        return true;
    }

    internal bool MarkSigned(DateTimeOffset at)
    {
        if (Status is SignerStatus.Signed or SignerStatus.Declined)
            return false;
        Status = SignerStatus.Signed;
        SignedAt = at;
        ViewedAt ??= at;
        return true;
    }

    internal bool MarkDeclined(DateTimeOffset at, string? reason)
    {
        if (Status is SignerStatus.Signed or SignerStatus.Declined)
            return false;
        Status = SignerStatus.Declined;
        DeclinedAt = at;
        DeclineReason = reason is { Length: > 500 } ? reason[..500] : reason;
        return true;
    }

    internal bool MarkDeliveryFailed()
    {
        if (Status != SignerStatus.Pending)
            return false;
        Status = SignerStatus.DeliveryFailed;
        return true;
    }

    public bool CanResend => Status is SignerStatus.Pending or SignerStatus.Viewed or SignerStatus.DeliveryFailed && AutentiquePublicId is not null;
}

public sealed class ContractEvent
{
    private ContractEvent() { } // EF Core

    public ContractEvent(Guid contractId, string type, Guid? signerId = null, Guid? actorUserId = null,
        Dictionary<string, string?>? payload = null, DateTimeOffset? occurredAt = null)
    {
        ContractId = contractId;
        Type = type;
        SignerId = signerId;
        ActorUserId = actorUserId;
        Payload = payload ?? [];
        OccurredAt = occurredAt ?? DateTimeOffset.UtcNow;
    }

    public long Id { get; private set; }
    public Guid ContractId { get; private set; }
    public Guid? SignerId { get; private set; }
    public string Type { get; private set; } = null!;
    public Dictionary<string, string?> Payload { get; private set; } = [];
    public Guid? ActorUserId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}

public sealed class DomainException(string message) : Exception(message);

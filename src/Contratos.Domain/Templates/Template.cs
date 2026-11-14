namespace Contratos.Domain.Templates;

public enum VariableType { Text, Number, Date, Money, CpfCnpj }

/// <summary>Uma variável do template, ex.: <c>{{contratante.nome}}</c>.</summary>
public sealed record TemplateVariable(string Key, string Label, VariableType Type, bool Required);

/// <summary>Ação do signatário, no vocabulário da Autentique.</summary>
public enum SignerAction { Sign, SignAsWitness, Approve, Recognize }

/// <summary>Papel sugerido pelo template (ex.: Contratante, Testemunha) e ordem padrão.</summary>
public sealed record SignerRoleSuggestion(string Role, SignerAction Action, int Order);

/// <summary>Cabeçalho do template. O conteúdo fica nas versões.</summary>
public sealed class Template
{
    public const int NameMaxLength = 120;
    public const int DescriptionMaxLength = 500;

    private Template() { } // EF Core

    public Template(Guid organizationId, string name, string? description, Guid createdBy)
    {
        Id = Guid.CreateVersion7();
        OrganizationId = organizationId;
        CreatedBy = createdBy;
        CreatedAt = UpdatedAt = DateTimeOffset.UtcNow;
        Rename(name, description);
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    /// <summary>Versão publicada que os contratos novos usam.</summary>
    public Guid? CurrentVersionId { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Concorrência otimista (coluna <c>xmin</c> do Postgres).</summary>
    public uint RowVersion { get; private set; }

    public void Rename(string name, string? description)
    {
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Touch();
    }

    public void Publish(TemplateVersion version)
    {
        if (version.TemplateId != Id)
            throw new InvalidOperationException("A versão é de outro template.");
        version.MarkPublished();
        CurrentVersionId = version.Id;
        Touch();
    }

    public void Archive() { ArchivedAt ??= DateTimeOffset.UtcNow; Touch(); }
    public void Unarchive() { ArchivedAt = null; Touch(); }
    public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

/// <summary>
/// Conteúdo de um template. Sem <see cref="PublishedAt"/> é o rascunho (no máximo um por template);
/// depois de publicada é imutável, e os contratos guardam o Id da versão que usaram.
/// </summary>
public sealed class TemplateVersion
{
    private TemplateVersion() { } // EF Core

    public TemplateVersion(Guid templateId, int version, Guid createdBy)
    {
        Id = Guid.CreateVersion7();
        TemplateId = templateId;
        Version = version;
        CreatedBy = createdBy;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TemplateId { get; private set; }
    public int Version { get; private set; }
    public string BodyHtml { get; private set; } = "";
    public List<TemplateVariable> Variables { get; private set; } = [];
    public List<SignerRoleSuggestion> DefaultSignerRoles { get; private set; } = [];
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public bool IsDraft => PublishedAt is null;

    public void Update(string bodyHtml, IEnumerable<TemplateVariable> variables, IEnumerable<SignerRoleSuggestion> roles)
    {
        if (!IsDraft)
            throw new InvalidOperationException("Versão publicada não pode ser alterada.");
        BodyHtml = bodyHtml;
        Variables = variables.ToList();
        DefaultSignerRoles = roles.OrderBy(r => r.Order).ToList();
    }

    internal void MarkPublished() => PublishedAt ??= DateTimeOffset.UtcNow;
}

namespace Contratos.Domain.Organizations;

/// <summary>Uma conta cliente do SaaS. Todo registro de negócio carrega o Id dela.</summary>
public sealed class Organization
{
    public const int NameMaxLength = 120;
    public const int SlugMaxLength = 60;
    public const int MessageMaxLength = 500;

    public static readonly string[] Reminders = ["DAILY", "WEEKLY"];
    public static readonly string[] Languages = ["pt-BR", "en-US"];

    private Organization() { } // EF Core

    public Organization(string name, string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("O slug da organização é obrigatório.", nameof(slug));

        Id = Guid.CreateVersion7();
        Rename(name);
        Slug = slug;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;

    /// <summary>Pasta opcional na conta Autentique da plataforma.</summary>
    public string? AutentiqueFolderId { get; private set; }

    /// <summary>Padrões de envio (lembrete, mensagem, idioma), guardados em jsonb.</summary>
    public OrganizationSettings Settings { get; private set; } = new();

    /// <summary>Último número de contrato emitido; o próximo é este + 1.</summary>
    public int ContractCounter { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("O nome da organização é obrigatório.", nameof(name));
        Name = name.Trim();
    }

    public void UpdateSettings(string? reminder, string? defaultMessage, string language)
    {
        if (reminder is not null && !Reminders.Contains(reminder))
            throw new ArgumentException("Lembrete inválido.", nameof(reminder));
        if (!Languages.Contains(language))
            throw new ArgumentException("Idioma inválido.", nameof(language));

        Settings = new OrganizationSettings
        {
            Reminder = reminder,
            DefaultMessage = string.IsNullOrWhiteSpace(defaultMessage) ? null : defaultMessage.Trim(),
            Language = language,
        };
    }
}

public sealed class OrganizationSettings
{
    /// <summary>Lembrete enviado pela Autentique: null, "DAILY" ou "WEEKLY".</summary>
    public string? Reminder { get; set; }
    public string? DefaultMessage { get; set; }
    public string Language { get; set; } = "pt-BR";
}

using Contratos.Domain.Billing;

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

    /// <summary>Plano atual (id do catálogo em <see cref="Plans"/>).</summary>
    public string PlanId { get; private set; } = Plans.FreeId;

    /// <summary>
    /// Ciclo pago da assinatura, quando há (vem do provedor de cobrança). Sem ele, ou fora dele,
    /// a cota vale pelo mês do calendário no horário de Brasília.
    /// </summary>
    public DateTimeOffset? PlanPeriodStart { get; private set; }
    public DateTimeOffset? PlanPeriodEnd { get; private set; }

    public Plan Plan => Plans.Get(PlanId);

    public void ChangePlan(string planId, DateTimeOffset? periodStart = null, DateTimeOffset? periodEnd = null)
    {
        var plan = Plans.Find(planId) ?? throw new ArgumentException("Plano desconhecido.", nameof(planId));
        if ((periodStart is null) != (periodEnd is null) || periodStart >= periodEnd)
            throw new ArgumentException("Informe início e fim do ciclo, com o fim depois do início.", nameof(periodEnd));
        PlanId = plan.Id;
        PlanPeriodStart = periodStart;
        PlanPeriodEnd = periodEnd;
    }

    /// <summary>Ciclo de cota que contém <paramref name="now"/>.</summary>
    public (DateTimeOffset Start, DateTimeOffset End) PeriodAt(DateTimeOffset now)
    {
        if (PlanPeriodStart is { } start && PlanPeriodEnd is { } end && start <= now && now < end)
            return (start, end);
        var brt = TimeSpan.FromHours(-3);
        var local = now.ToOffset(brt);
        var monthStart = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, brt);
        return (monthStart.ToUniversalTime(), monthStart.AddMonths(1).ToUniversalTime());
    }

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

using Contratos.Application.Email;
using Contratos.Application.Signatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Signatures;

public sealed class BalanceAlertOptions
{
    public const string Section = "SignatureBalance";

    /// <summary>Abaixo disto (documentos restantes na conta da plataforma) o saldo é considerado baixo.</summary>
    public int LowDocuments { get; set; } = 20;
    /// <summary>De quanto em quanto tempo consultar o saldo no provedor.</summary>
    public int CheckIntervalHours { get; set; } = 6;
    /// <summary>Quem recebe o alerta (equipe da plataforma, não os clientes). Vazio: só log e /health/ready.</summary>
    public string? AlertEmail { get; set; }
}

/// <summary>
/// Saldo da conta da plataforma no provedor de assinatura. Uma conta atende todos os clientes, então ficar sem
/// créditos para todo mundo: o worker consulta periodicamente, o /health/ready fica degradado com saldo baixo e
/// a equipe recebe no máximo um e-mail por dia. Falta de créditos num envio dispara o alerta na hora.
/// </summary>
public sealed class ProviderBalanceMonitor(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<BalanceAlertOptions> options,
    ILogger<ProviderBalanceMonitor> logger)
{
    private readonly Lock _lock = new();
    private DateTimeOffset _nextCheck = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAlert = DateTimeOffset.MinValue;

    public ProviderBalance? Last { get; private set; }
    public bool OutOfCredits { get; private set; }

    public bool IsLow =>
        OutOfCredits || (Last?.Documents is { } documents && documents < options.Value.LowDocuments);

    /// <summary>Chamado a cada rodada do worker; só consulta o provedor quando dá o intervalo.</summary>
    public async Task CheckIfDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        lock (_lock)
        {
            if (now < _nextCheck)
                return;
            _nextCheck = now.AddHours(Math.Max(0, options.Value.CheckIntervalHours));
        }

        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISignatureProvider>();
        ProviderBalance? balance;
        try
        {
            balance = await provider.GetBalanceAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Não foi possível consultar o saldo do provedor de assinatura");
            return;
        }
        if (balance is null)
            return; // provedor sem saldo (fake)

        Last = balance;
        if (balance.Documents is > 0)
            OutOfCredits = false;
        if (IsLow)
            await AlertAsync(scope.ServiceProvider, $"restam {balance.Documents?.ToString() ?? "?"} documentos (verificações extras: {balance.Credits?.ToString() ?? "?"})", ct);
    }

    /// <summary>O provedor recusou um envio por falta de créditos.</summary>
    public async Task ReportOutOfCreditsAsync(CancellationToken ct)
    {
        OutOfCredits = true;
        lock (_lock)
            _nextCheck = DateTimeOffset.MinValue; // confere o saldo de novo na próxima rodada
        await using var scope = scopes.CreateAsyncScope();
        await AlertAsync(scope.ServiceProvider, "um envio foi recusado por falta de créditos; os contratos ficam na fila tentando de novo", ct);
    }

    private async Task AlertAsync(IServiceProvider services, string detail, CancellationToken ct)
    {
        logger.LogWarning("Saldo baixo no provedor de assinatura: {Detail}", detail);
        var to = options.Value.AlertEmail;
        var now = clock.GetUtcNow();
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(to) || now - _lastAlert < TimeSpan.FromHours(24))
                return;
            _lastAlert = now;
        }

        var subject = "Saldo baixo na Autentique";
        var text = $"""
            O saldo da conta da plataforma na Autentique está baixo: {detail}.

            Sem créditos, nenhum cliente consegue enviar contratos para assinatura.
            Compre créditos ou ajuste o plano em https://painel.autentique.com.br/perfil/planos.

            Este aviso é enviado no máximo uma vez por dia.
            """;
        var html = $"<p>O saldo da conta da plataforma na Autentique está baixo: <strong>{System.Net.WebUtility.HtmlEncode(detail)}</strong>.</p>"
            + "<p>Sem créditos, nenhum cliente consegue enviar contratos para assinatura. "
            + "Compre créditos ou ajuste o plano em <a href=\"https://painel.autentique.com.br/perfil/planos\">painel.autentique.com.br/perfil/planos</a>.</p>"
            + "<p style=\"color:#666\">Este aviso é enviado no máximo uma vez por dia.</p>";
        try
        {
            await services.GetRequiredService<IEmailSender>().SendAsync(new EmailMessage(to!, "Equipe", subject, html, text), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Falha ao enviar o alerta de saldo baixo");
            lock (_lock)
                _lastAlert = DateTimeOffset.MinValue;
        }
    }
}

/// <summary>Saldo baixo (ou zerado) no provedor deixa o /health/ready degradado. Usa o último saldo consultado.</summary>
internal sealed class SignatureBalanceHealthCheck(ProviderBalanceMonitor monitor) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        Task.FromResult(monitor.IsLow
            ? new HealthCheckResult(context.Registration.FailureStatus, "Saldo baixo no provedor de assinatura")
            : HealthCheckResult.Healthy());
}

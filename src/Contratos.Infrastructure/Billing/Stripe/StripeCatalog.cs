using Contratos.Domain.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Plan = Contratos.Domain.Billing.Plan;
using BillingPortal = Stripe.BillingPortal;

namespace Contratos.Infrastructure.Billing.Stripe;

public sealed class StripeOptions
{
    public const string Section = "Stripe";

    /// <summary>sk_test_... ou sk_live_... (ou uma chave restrita rk_...). Vazio: sem assinatura online.</summary>
    public string? SecretKey { get; set; }
    /// <summary>whsec_... do endpoint de webhook cadastrado no painel.</summary>
    public string? WebhookSecret { get; set; }
    /// <summary>Só para testes: aponta o SDK para um Stripe falso.</summary>
    public string? ApiBase { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(SecretKey);
}

/// <summary>
/// Planos no Stripe a partir do catálogo em código: um produto por plano pago e um preço mensal e um anual,
/// achados pela lookup key. Preço mudou no código: cria um preço novo e a lookup key passa para ele (quem já
/// assina continua no preço antigo até trocar). Também mantém a configuração do portal do cliente.
/// Roda uma vez por processo, na primeira necessidade.
/// </summary>
public sealed class StripeCatalog(StripeClient stripe, ILogger<StripeCatalog> logger)
{
    public const string Monthly = "month";
    public const string Annual = "year";
    private const string AppTag = "contratos";

    public sealed record Synced(IReadOnlyDictionary<(string Plan, string Interval), string> Prices, string PortalConfigurationId);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Synced? _synced;

    public static string ProductId(Plan plan) => $"{AppTag}_{plan.Id}";
    public static string LookupKey(Plan plan, string interval) => $"{AppTag}_{plan.Id}_{(interval == Annual ? "anual" : "mensal")}";
    public static long Cents(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    private static IEnumerable<Plan> PaidPlans => Plans.All.Where(p => p.MonthlyPrice > 0);

    public async Task<Synced> EnsureAsync(CancellationToken ct)
    {
        if (_synced is { } done)
            return done;
        await _gate.WaitAsync(ct);
        try
        {
            return _synced ??= await SyncAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Synced> SyncAsync(CancellationToken ct)
    {
        var wanted = PaidPlans
            .SelectMany(p => new[] { (Plan: p, Interval: Monthly, Amount: p.MonthlyPrice), (Plan: p, Interval: Annual, Amount: p.AnnualPrice) })
            .ToList();
        var existing = await stripe.V1.Prices.ListAsync(new PriceListOptions
        {
            LookupKeys = wanted.Select(w => LookupKey(w.Plan, w.Interval)).ToList(),
            Active = true,
            Limit = 100,
        }, cancellationToken: ct);

        var prices = new Dictionary<(string, string), string>();
        var ensuredProducts = new HashSet<string>();
        foreach (var (plan, interval, amount) in wanted)
        {
            var key = LookupKey(plan, interval);
            var match = existing.Data.FirstOrDefault(p => p.LookupKey == key);
            if (match is not null && match.UnitAmount == Cents(amount) && match.Currency == "brl" && match.Recurring?.Interval == interval)
            {
                prices[(plan.Id, interval)] = match.Id;
                continue;
            }

            if (ensuredProducts.Add(plan.Id))
                await EnsureProductAsync(plan, ct);
            var created = await stripe.V1.Prices.CreateAsync(new PriceCreateOptions
            {
                Product = ProductId(plan),
                Currency = "brl",
                UnitAmount = Cents(amount),
                Recurring = new PriceRecurringOptions { Interval = interval },
                LookupKey = key,
                TransferLookupKey = true,
                Nickname = $"{plan.Name} {(interval == Annual ? "anual" : "mensal")}",
                Metadata = new() { ["plan"] = plan.Id, ["interval"] = interval, ["app"] = AppTag },
            }, cancellationToken: ct);
            logger.LogInformation("Preço criado no Stripe: {LookupKey} = {Amount} ({PriceId})", key, amount, created.Id);
            prices[(plan.Id, interval)] = created.Id;
        }

        var portal = await EnsurePortalAsync(prices, ct);
        return new Synced(prices, portal);
    }

    private async Task EnsureProductAsync(Plan plan, CancellationToken ct)
    {
        try
        {
            await stripe.V1.Products.GetAsync(ProductId(plan), cancellationToken: ct);
        }
        catch (StripeException e) when (e.StripeError?.Code == "resource_missing")
        {
            await stripe.V1.Products.CreateAsync(new ProductCreateOptions
            {
                Id = ProductId(plan),
                Name = $"Plano {plan.Name}",
                Metadata = new() { ["plan"] = plan.Id, ["app"] = AppTag },
            }, cancellationToken: ct);
        }
    }

    /// <summary>
    /// Portal: trocar de plano (upgrade cobra a diferença na hora; downgrade e troca para mensal ficam para o
    /// fim do ciclo), trocar cartão, atualizar dados de cobrança, ver faturas e cancelar no fim do ciclo.
    /// </summary>
    private async Task<string> EnsurePortalAsync(Dictionary<(string, string), string> prices, CancellationToken ct)
    {
        var features = new BillingPortal.ConfigurationFeaturesOptions
        {
            CustomerUpdate = new() { Enabled = true, AllowedUpdates = ["email", "address", "tax_id"] },
            InvoiceHistory = new() { Enabled = true },
            PaymentMethodUpdate = new() { Enabled = true },
            SubscriptionCancel = new() { Enabled = true, Mode = "at_period_end" },
            SubscriptionUpdate = new()
            {
                Enabled = true,
                DefaultAllowedUpdates = ["price"],
                ProrationBehavior = "always_invoice",
                Products = PaidPlans.Select(p => new BillingPortal.ConfigurationFeaturesSubscriptionUpdateProductOptions
                {
                    Product = ProductId(p),
                    Prices = [prices[(p.Id, Monthly)], prices[(p.Id, Annual)]],
                }).ToList(),
                ScheduleAtPeriodEnd = new()
                {
                    Conditions =
                    [
                        new() { Type = "decreasing_item_amount" },
                        new() { Type = "shortening_interval" },
                    ],
                },
            },
        };

        var configs = await stripe.V1.BillingPortal.Configurations.ListAsync(new() { Active = true, Limit = 100 }, cancellationToken: ct);
        var ours = configs.Data.FirstOrDefault(c => c.Metadata?.GetValueOrDefault("app") == AppTag);
        if (ours is not null)
        {
            await stripe.V1.BillingPortal.Configurations.UpdateAsync(ours.Id, new() { Features = features }, cancellationToken: ct);
            return ours.Id;
        }
        var created = await stripe.V1.BillingPortal.Configurations.CreateAsync(new BillingPortal.ConfigurationCreateOptions
        {
            Name = "Contratos",
            Features = features,
            Metadata = new() { ["app"] = AppTag },
        }, cancellationToken: ct);
        return created.Id;
    }
}

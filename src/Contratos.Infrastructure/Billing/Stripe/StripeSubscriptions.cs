using System.Text.Json;
using Contratos.Application.Billing;
using Contratos.Application.Common;
using Contratos.Domain.Billing;
using Contratos.Domain.Organizations;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Plan = Contratos.Domain.Billing.Plan;
using Checkout = Stripe.Checkout;
using BillingPortal = Stripe.BillingPortal;

namespace Contratos.Infrastructure.Billing.Stripe;

/// <summary>
/// Copia o estado de uma assinatura do Stripe para a organização. Sempre busca a assinatura atual na API
/// (nunca confia no corpo do webhook): eventos fora de ordem ou repetidos dão o mesmo resultado.
/// </summary>
public sealed class StripeSubscriptionSync(AppDbContext db, StripeClient stripe, ILogger<StripeSubscriptionSync> logger)
{
    public const string OrganizationMetadata = "organization_id";

    public async Task<Organization?> SyncAsync(string subscriptionId, CancellationToken ct)
    {
        var sub = await stripe.V1.Subscriptions.GetAsync(subscriptionId, cancellationToken: ct);
        var org = await FindOrganizationAsync(sub, ct);
        if (org is null)
        {
            logger.LogWarning("Assinatura {SubscriptionId} do Stripe sem organização correspondente", subscriptionId);
            return null;
        }

        // Evento de uma assinatura antiga (a organização já tem outra vigente): não mexe na atual.
        if (org.StripeSubscriptionId is { } current && current != sub.Id && org.HasLiveSubscription)
        {
            logger.LogInformation("Ignorando assinatura {Old}: a organização {OrgId} está na {Current}", sub.Id, org.Id, current);
            return org;
        }

        org.SetStripeCustomer(sub.CustomerId);
        switch (sub.Status)
        {
            case "active" or "trialing" or "past_due":
            {
                var item = sub.Items.Data.FirstOrDefault()
                    ?? throw new InvalidOperationException($"Assinatura {sub.Id} sem itens.");
                var plan = PlanOf(item.Price)
                    ?? throw new InvalidOperationException($"Preço {item.Price?.Id} da assinatura {sub.Id} não corresponde a um plano.");
                org.ApplySubscription(sub.Id, sub.Status, plan.Id, item.Price!.Recurring?.Interval ?? StripeCatalog.Monthly,
                    ToUtc(item.CurrentPeriodStart), ToUtc(item.CurrentPeriodEnd), sub.CancelAtPeriodEnd);
                break;
            }
            case "canceled" or "unpaid" or "incomplete_expired":
                org.EndSubscription(sub.Status);
                break;
            default: // incomplete, paused: ainda não dá (ou deixou de dar) direito ao plano pago
                org.RecordSubscriptionStatus(sub.Id, sub.Status);
                if (sub.Status == "paused" && org.PlanId != Plans.FreeId)
                    org.EndSubscription(sub.Status);
                break;
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Assinatura {SubscriptionId} sincronizada: {Status}, plano {Plan}", sub.Id, sub.Status, org.PlanId);
        return org;
    }

    private async Task<Organization?> FindOrganizationAsync(Subscription sub, CancellationToken ct)
    {
        if (sub.Metadata?.GetValueOrDefault(OrganizationMetadata) is { } raw && Guid.TryParse(raw, out var orgId))
            return await db.Organizations.SingleOrDefaultAsync(o => o.Id == orgId, ct);
        return await db.Organizations.SingleOrDefaultAsync(o => o.StripeCustomerId == sub.CustomerId, ct);
    }

    internal static Plan? PlanOf(Price? price)
    {
        if (price is null)
            return null;
        if (Plans.Find(price.Metadata?.GetValueOrDefault("plan")) is { } byMetadata)
            return byMetadata;
        return Plans.All.FirstOrDefault(p =>
            price.LookupKey == StripeCatalog.LookupKey(p, StripeCatalog.Monthly) || price.LookupKey == StripeCatalog.LookupKey(p, StripeCatalog.Annual));
    }

    private static DateTimeOffset ToUtc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc));

    // ---------- Webhooks ----------

    public static readonly HashSet<string> HandledEvents =
    [
        "checkout.session.completed",
        "customer.subscription.created",
        "customer.subscription.updated",
        "customer.subscription.deleted",
        "customer.subscription.paused",
        "customer.subscription.resumed",
        "invoice.paid",
        "invoice.payment_failed",
    ];

    /// <summary>Processa um evento guardado na webhook_inbox: descobre a assinatura e sincroniza.</summary>
    public async Task HandleEventAsync(string payload, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString();
        var obj = root.GetProperty("data").GetProperty("object");
        var subscriptionId = SubscriptionIdOf(type, obj);
        if (subscriptionId is null)
        {
            logger.LogInformation("Evento {Type} do Stripe sem assinatura; ignorado", type);
            return;
        }
        await SyncAsync(subscriptionId, ct);
    }

    internal static string? SubscriptionIdOf(string? type, JsonElement obj)
    {
        if (type?.StartsWith("customer.subscription.", StringComparison.Ordinal) == true)
            return Str(obj, "id");
        if (type == "checkout.session.completed")
            return Str(obj, "mode") == "subscription" ? Str(obj, "subscription") : null;
        if (type?.StartsWith("invoice.", StringComparison.Ordinal) == true)
        {
            // Versões novas da API: invoice.parent.subscription_details.subscription; antigas: invoice.subscription.
            if (obj.TryGetProperty("parent", out var parent) && parent.ValueKind == JsonValueKind.Object
                && parent.TryGetProperty("subscription_details", out var details) && details.ValueKind == JsonValueKind.Object)
                return Str(details, "subscription");
            return Str(obj, "subscription");
        }
        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>Assinar, confirmar a volta do pagamento e abrir o portal, para a organização logada.</summary>
internal sealed class StripeSubscriptionService(
    AppDbContext db,
    ICurrentTenant tenant,
    StripeClient stripe,
    StripeCatalog catalog,
    StripeSubscriptionSync sync,
    BillingReader reader,
    IOptions<AppOptions> app,
    ILogger<StripeSubscriptionService> logger) : ISubscriptionService
{
    private string SettingsUrl => $"{app.Value.PublicBaseUrl.TrimEnd('/')}/settings.html";

    public async Task<Result<RedirectDto>> CheckoutAsync(CheckoutRequest req, CancellationToken ct)
    {
        var plan = Plans.Find(req.Plan);
        var errors = new ValidationErrors();
        if (plan is null || plan.MonthlyPrice == 0)
            errors.Add("plan", "Escolha um plano pago.");
        if (req.Interval is not (StripeCatalog.Monthly or StripeCatalog.Annual))
            errors.Add("interval", "Escolha mensal ou anual.");
        if (!errors.IsEmpty)
            return Error.Validation(errors);

        var org = await db.Organizations.SingleAsync(o => o.Id == tenant.RequireOrganizationId(), ct);
        if (org.HasLiveSubscription)
            return Error.Conflict("Sua empresa já tem uma assinatura. Para trocar de plano, use \"Gerenciar assinatura\".", "already_subscribed");

        try
        {
            var synced = await catalog.EnsureAsync(ct);
            if (org.StripeCustomerId is null)
            {
                var email = await db.Users.Where(u => u.Id == tenant.UserId).Select(u => u.Email).SingleAsync(ct);
                var customer = await stripe.V1.Customers.CreateAsync(new CustomerCreateOptions
                {
                    Name = org.Name,
                    Email = email,
                    PreferredLocales = ["pt-BR"],
                    Metadata = new() { [StripeSubscriptionSync.OrganizationMetadata] = org.Id.ToString() },
                }, new RequestOptions { IdempotencyKey = $"customer-{org.Id}" }, ct);
                org.SetStripeCustomer(customer.Id);
                await db.SaveChangesAsync(ct);
            }

            var metadata = new Dictionary<string, string> { [StripeSubscriptionSync.OrganizationMetadata] = org.Id.ToString() };
            var session = await stripe.V1.Checkout.Sessions.CreateAsync(new Checkout.SessionCreateOptions
            {
                Mode = "subscription",
                Customer = org.StripeCustomerId,
                ClientReferenceId = org.Id.ToString(),
                LineItems = [new() { Price = synced.Prices[(plan!.Id, req.Interval!)], Quantity = 1 }],
                // Só cartão: o pagamento confirma na hora e o plano ativa na volta.
                AllowedPaymentMethodTypes = ["card"],
                Locale = "pt-BR",
                BillingAddressCollection = "required",
                TaxIdCollection = new() { Enabled = true },
                CustomerUpdate = new() { Name = "auto", Address = "auto" },
                Metadata = metadata,
                SubscriptionData = new() { Metadata = metadata },
                SuccessUrl = $"{SettingsUrl}?checkout=success&session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl = $"{SettingsUrl}?checkout=cancelled",
            }, cancellationToken: ct);
            return new RedirectDto(session.Url);
        }
        catch (StripeException e)
        {
            logger.LogError(e, "Stripe recusou o checkout da organização {OrgId}: {Code}", org.Id, e.StripeError?.Code);
            return Error.Conflict("Não foi possível abrir o pagamento agora. Tente de novo em instantes.", "payment_unavailable");
        }
    }

    public async Task<Result<BillingDto>> ConfirmCheckoutAsync(ConfirmCheckoutRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.SessionId) || !req.SessionId.StartsWith("cs_", StringComparison.Ordinal))
            return Error.Validation("sessionId", "Sessão de pagamento inválida.");

        var orgId = tenant.RequireOrganizationId();
        Checkout.Session session;
        try
        {
            session = await stripe.V1.Checkout.Sessions.GetAsync(req.SessionId, cancellationToken: ct);
        }
        catch (StripeException e) when (e.StripeError?.Code == "resource_missing")
        {
            return Error.NotFound("Sessão de pagamento não encontrada.");
        }
        // A sessão precisa ser desta organização: não dá para "ativar" com a sessão de outra empresa.
        if (session.ClientReferenceId != orgId.ToString())
            return Error.NotFound("Sessão de pagamento não encontrada.");
        if (session.Status == "complete" && session.SubscriptionId is { } subscriptionId)
            await sync.SyncAsync(subscriptionId, ct);

        var org = await db.Organizations.SingleAsync(o => o.Id == orgId, ct);
        return await reader.BuildAsync(org, ct);
    }

    public async Task<Result<RedirectDto>> PortalAsync(CancellationToken ct)
    {
        var org = await db.Organizations.SingleAsync(o => o.Id == tenant.RequireOrganizationId(), ct);
        if (org.StripeCustomerId is null)
            return Error.Conflict("Sua empresa ainda não tem assinatura.", "no_subscription");
        try
        {
            var synced = await catalog.EnsureAsync(ct);
            var session = await stripe.V1.BillingPortal.Sessions.CreateAsync(new BillingPortal.SessionCreateOptions
            {
                Customer = org.StripeCustomerId,
                Configuration = synced.PortalConfigurationId,
                Locale = "pt-BR",
                ReturnUrl = SettingsUrl,
            }, cancellationToken: ct);
            return new RedirectDto(session.Url);
        }
        catch (StripeException e)
        {
            logger.LogError(e, "Stripe recusou o portal da organização {OrgId}: {Code}", org.Id, e.StripeError?.Code);
            return Error.Conflict("Não foi possível abrir o portal de assinatura agora. Tente de novo em instantes.", "payment_unavailable");
        }
    }
}

/// <summary>Sem Stripe configurado: a tela mostra o contato em vez dos botões.</summary>
internal sealed class DisabledSubscriptionService : ISubscriptionService
{
    private static readonly Error Disabled = Error.Conflict("A assinatura online não está disponível.", "checkout_disabled");
    public Task<Result<RedirectDto>> CheckoutAsync(CheckoutRequest request, CancellationToken ct) => Task.FromResult<Result<RedirectDto>>(Disabled);
    public Task<Result<BillingDto>> ConfirmCheckoutAsync(ConfirmCheckoutRequest request, CancellationToken ct) => Task.FromResult<Result<BillingDto>>(Disabled);
    public Task<Result<RedirectDto>> PortalAsync(CancellationToken ct) => Task.FromResult<Result<RedirectDto>>(Disabled);
}

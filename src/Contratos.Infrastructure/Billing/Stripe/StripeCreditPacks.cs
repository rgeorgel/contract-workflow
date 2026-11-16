using Contratos.Domain.Billing;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Checkout = Stripe.Checkout;

namespace Contratos.Infrastructure.Billing.Stripe;

/// <summary>
/// Pacotes de contratos avulsos pagos no Stripe Checkout. Os créditos entram uma vez por sessão de pagamento
/// (a sessão é a referência, única por organização): a volta do pagamento e o webhook podem chegar os dois.
/// </summary>
public sealed class StripeCreditPacks(AppDbContext db, TimeProvider clock, ILogger<StripeCreditPacks> logger)
{
    public const string Kind = "credit_pack";
    public const string KindMetadata = "kind";
    public const string QuantityMetadata = "pack_quantity";

    public static CreditPack? Find(int? quantity) => Plans.Packs.FirstOrDefault(p => p.Quantity == quantity);

    /// <summary>Libera os créditos de uma sessão paga. Devolve a organização, ou null se a sessão não é de pacote pago.</summary>
    public async Task<Guid?> GrantAsync(Checkout.Session session, CancellationToken ct)
    {
        if (session.Mode != "payment" || session.Metadata?.GetValueOrDefault(KindMetadata) != Kind)
            return null;
        if (!Guid.TryParse(session.ClientReferenceId, out var orgId))
            return null;
        if (session.PaymentStatus != "paid")
        {
            logger.LogInformation("Sessão {SessionId} do pacote ainda não paga ({Status})", session.Id, session.PaymentStatus);
            return null;
        }

        var pack = Find(int.TryParse(session.Metadata.GetValueOrDefault(QuantityMetadata), out var q) ? q : null);
        // O valor cobrado precisa ser o do pacote: nunca libera crédito por um valor diferente do catálogo.
        if (pack is null || session.Currency != "brl" || session.AmountTotal != StripeCatalog.Cents(pack.Price))
        {
            logger.LogError("Sessão {SessionId} de pacote com quantidade ou valor inesperado ({Amount} {Currency})",
                session.Id, session.AmountTotal, session.Currency);
            return null;
        }

        if (await db.CreditGrants.AnyAsync(g => g.OrganizationId == orgId && g.Reference == session.Id, ct))
            return orgId;
        if (!await db.Organizations.AnyAsync(o => o.Id == orgId, ct))
            return null;

        var now = clock.GetUtcNow();
        db.CreditGrants.Add(new CreditGrant
        {
            OrganizationId = orgId,
            Quantity = pack.Quantity,
            Remaining = pack.Quantity,
            CreatedAt = now,
            ExpiresAt = now.AddMonths(Plans.CreditValidityMonths),
            Source = CreditSources.Stripe,
            Reference = session.Id,
            Note = $"Pacote de {pack.Quantity} {(pack.Quantity == 1 ? "contrato" : "contratos")}",
        });
        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Pacote de {Quantity} contratos liberado para {OrgId} ({SessionId})", pack.Quantity, orgId, session.Id);
        }
        catch (DbUpdateException)
        {
            // A volta do pagamento e o webhook ao mesmo tempo: o índice único deixou só um passar.
            db.ChangeTracker.Clear();
        }
        return orgId;
    }
}

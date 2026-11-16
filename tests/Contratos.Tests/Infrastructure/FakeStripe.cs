using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;

namespace Contratos.Tests.Infrastructure;

/// <summary>
/// Stripe de mentira no nível HTTP, para o SDK oficial: responde às rotas que a integração usa e guarda
/// as requisições. Também monta webhooks assinados como o Stripe enviaria.
/// </summary>
public sealed class FakeStripe : HttpMessageHandler
{
    public const string SecretKey = "sk_test_fake";
    public const string WebhookSecret = "whsec_test_fake";

    public sealed record Captured(HttpMethod Method, string Path, IReadOnlyDictionary<string, string> Form);

    public ConcurrentQueue<Captured> Requests { get; } = new();
    public ConcurrentDictionary<string, JsonObject> Prices { get; } = new();
    public ConcurrentDictionary<string, JsonObject> Products { get; } = new();
    public ConcurrentDictionary<string, JsonObject> Sessions { get; } = new();
    public ConcurrentDictionary<string, JsonObject> Subscriptions { get; } = new();
    public ConcurrentDictionary<string, JsonObject> PortalConfigs { get; } = new();

    public IEnumerable<Captured> Posts(string path) => Requests.Where(r => r.Method == HttpMethod.Post && r.Path == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var form = request.Content is null
            ? new Dictionary<string, string>()
            : ParseForm(await request.Content.ReadAsStringAsync(ct));
        Requests.Enqueue(new Captured(request.Method, path, form));
        if (request.Headers.Authorization?.Parameter != SecretKey)
            return Error(HttpStatusCode.Unauthorized, "authentication_required", "Invalid API Key");

        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
        var get = request.Method == HttpMethod.Get;
        var parts = path.Trim('/').Split('/'); // v1/...

        switch (parts[1..])
        {
            case ["prices"] when get:
            {
                var keys = query.AllKeys.Where(k => k?.StartsWith("lookup_keys", StringComparison.Ordinal) == true).Select(k => query[k]).ToHashSet();
                return List("/v1/prices", Prices.Values.Where(p => keys.Contains(p["lookup_key"]?.GetValue<string>()) && p["active"]!.GetValue<bool>()));
            }
            case ["prices"]:
            {
                var key = form.GetValueOrDefault("lookup_key");
                if (form.GetValueOrDefault("transfer_lookup_key") == "true")
                    foreach (var old in Prices.Values.Where(p => p["lookup_key"]?.GetValue<string>() == key))
                        old["lookup_key"] = null;
                var price = new JsonObject
                {
                    ["id"] = $"price_{Guid.NewGuid():N}"[..20], ["object"] = "price", ["active"] = true,
                    ["currency"] = form["currency"], ["unit_amount"] = long.Parse(form["unit_amount"]),
                    ["lookup_key"] = key, ["product"] = form["product"], ["type"] = "recurring",
                    ["recurring"] = new JsonObject { ["interval"] = form["recurring[interval]"], ["interval_count"] = 1 },
                    ["metadata"] = Metadata(form),
                };
                Prices[price["id"]!.GetValue<string>()] = price;
                return Ok(price);
            }
            case ["products", var id] when get:
                return Products.TryGetValue(id, out var found) ? Ok(found) : Error(HttpStatusCode.NotFound, "resource_missing", "No such product");
            case ["products"]:
            {
                var product = new JsonObject { ["id"] = form["id"], ["object"] = "product", ["name"] = form["name"], ["active"] = true, ["metadata"] = Metadata(form) };
                Products[form["id"]] = product;
                return Ok(product);
            }
            case ["billing_portal", "configurations"] when get:
                return List("/v1/billing_portal/configurations", PortalConfigs.Values);
            case ["billing_portal", "configurations", ..]:
            {
                var id = parts.Length > 3 ? parts[3] : $"bpc_{Guid.NewGuid():N}"[..20];
                var config = PortalConfigs.GetOrAdd(id, _ => new JsonObject { ["id"] = id, ["object"] = "billing_portal.configuration", ["active"] = true, ["metadata"] = Metadata(form) });
                config["features_form"] = new JsonObject(form.Where(kv => kv.Key.StartsWith("features", StringComparison.Ordinal))
                    .Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value)));
                return Ok(config);
            }
            case ["billing_portal", "sessions"]:
                return Ok(new JsonObject
                {
                    ["id"] = "bps_1", ["object"] = "billing_portal.session", ["customer"] = form["customer"],
                    ["configuration"] = form.GetValueOrDefault("configuration"), ["url"] = "https://billing.stripe.test/p/session_1",
                    ["return_url"] = form.GetValueOrDefault("return_url"),
                });
            case ["customers"]:
                return Ok(new JsonObject { ["id"] = $"cus_{Guid.NewGuid():N}"[..18], ["object"] = "customer", ["name"] = form.GetValueOrDefault("name"), ["metadata"] = Metadata(form) });
            case ["checkout", "sessions"]:
            {
                var id = $"cs_test_{Guid.NewGuid():N}";
                var session = new JsonObject
                {
                    ["id"] = id, ["object"] = "checkout.session", ["mode"] = form["mode"], ["status"] = "open",
                    ["customer"] = form.GetValueOrDefault("customer"), ["client_reference_id"] = form.GetValueOrDefault("client_reference_id"),
                    ["url"] = $"https://checkout.stripe.test/c/pay/{id}", ["subscription"] = null,
                    ["line_item_price"] = form.GetValueOrDefault("line_items[0][price]"),
                    ["subscription_metadata_org"] = form.GetValueOrDefault("subscription_data[metadata][organization_id]"),
                    ["metadata"] = Metadata(form),
                    ["payment_status"] = "unpaid",
                    ["currency"] = form.GetValueOrDefault("line_items[0][price_data][currency]") ?? "brl",
                    ["amount_total"] = long.TryParse(form.GetValueOrDefault("line_items[0][price_data][unit_amount]"), out var amount) ? amount : null,
                };
                Sessions[id] = session;
                return Ok(session);
            }
            case ["checkout", "sessions", var id] when get:
                return Sessions.TryGetValue(id, out var s) ? Ok(s) : Error(HttpStatusCode.NotFound, "resource_missing", "No such checkout.session");
            case ["subscriptions", var id] when get:
                return Subscriptions.TryGetValue(id, out var sub) ? Ok(sub) : Error(HttpStatusCode.NotFound, "resource_missing", "No such subscription");
            default:
                return Error(HttpStatusCode.NotFound, "resource_missing", $"Rota não simulada: {request.Method} {path}");
        }
    }

    // ---------- Simulação do lado do cliente ----------

    /// <summary>O cliente pagou: a sessão completa e nasce a assinatura no preço escolhido.</summary>
    public string CompleteCheckout(string sessionId, string status = "active")
    {
        var session = Sessions[sessionId];
        var subscriptionId = $"sub_{Guid.NewGuid():N}"[..20];
        Subscriptions[subscriptionId] = Subscription(subscriptionId, session["customer"]!.GetValue<string>(),
            session["line_item_price"]!.GetValue<string>(), status, session["subscription_metadata_org"]?.GetValue<string>());
        session["status"] = "complete";
        session["subscription"] = subscriptionId;
        return subscriptionId;
    }

    /// <summary>O cliente pagou o pacote avulso: a sessão de pagamento único completa e fica paga.</summary>
    public void CompletePackCheckout(string sessionId)
    {
        Sessions[sessionId]["status"] = "complete";
        Sessions[sessionId]["payment_status"] = "paid";
    }

    /// <summary>Muda a assinatura como o portal ou a cobrança fariam (status, preço, cancelamento).</summary>
    /// <param name="cancelAt">Cancelamento agendado como o portal faz nas versões novas da API (cancel_at, sem cancel_at_period_end).</param>
    public void UpdateSubscription(string id, string? status = null, string? priceId = null, bool? cancelAtPeriodEnd = null,
        DateTimeOffset? cancelAt = null, bool clearCancelAt = false)
    {
        var sub = Subscriptions[id];
        var price = priceId ?? sub["items"]!["data"]![0]!["price"]!["id"]!.GetValue<string>();
        var currentCancelAt = sub["cancel_at"]?.GetValue<long>() is { } unix ? DateTimeOffset.FromUnixTimeSeconds(unix) : (DateTimeOffset?)null;
        Subscriptions[id] = Subscription(id, sub["customer"]!.GetValue<string>(), price,
            status ?? sub["status"]!.GetValue<string>(), sub["metadata"]?["organization_id"]?.GetValue<string>(),
            cancelAtPeriodEnd ?? sub["cancel_at_period_end"]!.GetValue<bool>(),
            clearCancelAt ? null : cancelAt ?? currentCancelAt);
    }

    public string PriceId(string lookupKey) =>
        Prices.Values.Single(p => p["lookup_key"]?.GetValue<string>() == lookupKey)["id"]!.GetValue<string>();

    public static readonly DateTimeOffset PeriodStart = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds());
    public static DateTimeOffset PeriodEnd(string interval) => interval == "year" ? PeriodStart.AddYears(1) : PeriodStart.AddMonths(1);

    private JsonObject Subscription(string id, string customer, string priceId, string status, string? orgId, bool cancelAtPeriodEnd = false, DateTimeOffset? cancelAt = null)
    {
        var price = Prices[priceId].DeepClone().AsObject();
        var interval = price["recurring"]!["interval"]!.GetValue<string>();
        return new JsonObject
        {
            ["id"] = id, ["object"] = "subscription", ["status"] = status, ["customer"] = customer,
            ["cancel_at_period_end"] = cancelAtPeriodEnd,
            ["cancel_at"] = cancelAt?.ToUnixTimeSeconds(),
            ["metadata"] = orgId is null ? new JsonObject() : new JsonObject { ["organization_id"] = orgId },
            ["items"] = new JsonObject
            {
                ["object"] = "list", ["has_more"] = false, ["url"] = "/v1/subscription_items",
                ["data"] = new JsonArray(new JsonObject
                {
                    ["id"] = "si_1", ["object"] = "subscription_item", ["quantity"] = 1, ["price"] = price,
                    ["current_period_start"] = PeriodStart.ToUnixTimeSeconds(),
                    ["current_period_end"] = PeriodEnd(interval).ToUnixTimeSeconds(),
                }),
            },
        };
    }

    /// <summary>Webhook assinado como o Stripe envia (cabeçalho Stripe-Signature).</summary>
    public static (string Body, string Signature) Webhook(string type, JsonObject dataObject, string? eventId = null, string secret = WebhookSecret)
    {
        var body = new JsonObject
        {
            ["id"] = eventId ?? $"evt_{Guid.NewGuid():N}", ["object"] = "event", ["type"] = type,
            ["api_version"] = "2025-01-27.acacia", // de propósito diferente da do SDK: o corpo não é usado, só o id
            ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["data"] = new JsonObject { ["object"] = dataObject },
        }.ToJsonString();
        return (body, global::Stripe.EventUtility.GenerateSignatureHeader(body, secret));
    }

    // ---------- Apoio ----------

    private static Dictionary<string, string> ParseForm(string body)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            result[Uri.UnescapeDataString(kv[0].Replace('+', ' '))] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
        }
        return result;
    }

    private static JsonObject Metadata(Dictionary<string, string> form) =>
        new(form.Where(kv => kv.Key.StartsWith("metadata[", StringComparison.Ordinal))
            .Select(kv => KeyValuePair.Create(kv.Key[9..^1], (JsonNode?)kv.Value)));

    private static HttpResponseMessage List(string url, IEnumerable<JsonObject> items) =>
        Ok(new JsonObject { ["object"] = "list", ["url"] = url, ["has_more"] = false, ["data"] = new JsonArray(items.Select(i => (JsonNode)i.DeepClone()).ToArray()) });

    private static HttpResponseMessage Ok(JsonObject body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Error(HttpStatusCode status, string code, string message) =>
        new(status)
        {
            Content = new StringContent(new JsonObject
            {
                ["error"] = new JsonObject { ["type"] = "invalid_request_error", ["code"] = code, ["message"] = message },
            }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
}

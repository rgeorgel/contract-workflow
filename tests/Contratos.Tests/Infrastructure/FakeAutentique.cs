using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Contratos.Tests.Infrastructure;

/// <summary>
/// Autentique de mentira no nível HTTP: responde à API GraphQL como a documentação descreve e guarda as
/// requisições para os testes conferirem. Também monta webhooks assinados como o painel enviaria.
/// </summary>
public sealed class FakeAutentique : HttpMessageHandler
{
    public const string ApiKey = "test-api-key";
    public const string WebhookSecret = "test-webhook-secret";
    /// <summary>Secret do segundo endpoint (o painel separa eventos de documento e de assinatura).</summary>
    public const string SecondWebhookSecret = "01MSEGUNDOENDPOINTHKCK";
    public const string SignedPdfUrl = "https://storage.googleapis.test/autentique/assinado.pdf";
    public static readonly byte[] SignedPdf = "%PDF-1.7 documento assinado"u8.ToArray();

    public sealed record Captured(string? Query, JsonNode? Variables, byte[]? File, string? Authorization, string Url);

    public ConcurrentQueue<Captured> Requests { get; } = new();

    /// <summary>Documentos criados: id → public_ids na ordem dos signatários.</summary>
    public ConcurrentDictionary<string, List<string>> Documents { get; } = new();

    /// <summary>Resposta da query document(id) para a reconciliação: public_id → (viewed, signed).</summary>
    public ConcurrentDictionary<string, (DateTimeOffset? Viewed, DateTimeOffset? Signed)> SignatureState { get; } = new();

    /// <summary>Próxima resposta de erro (uma vez), para simular falhas.</summary>
    public Func<HttpResponseMessage>? FailNext { get; set; }

    /// <summary>Enquanto definido, todo createDocument responde com isto.</summary>
    public Func<HttpResponseMessage>? FailCreateDocument { get; set; }

    public IEnumerable<Captured> Mutations(string name) => Requests.Where(r => r.Query?.Contains(name, StringComparison.Ordinal) == true);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var auth = request.Headers.Authorization?.ToString();
        if (request.Method == HttpMethod.Get)
        {
            Requests.Enqueue(new Captured(null, null, null, auth, request.RequestUri!.ToString()));
            return request.RequestUri!.ToString() == SignedPdfUrl
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(SignedPdf) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        string query;
        JsonNode? variables;
        byte[]? file = null;
        if (request.Content is MultipartFormDataContent multipart)
        {
            var parts = new Dictionary<string, HttpContent>();
            foreach (var part in multipart)
                parts[part.Headers.ContentDisposition!.Name!.Trim('"')] = part;
            var operations = JsonNode.Parse(await parts["operations"].ReadAsStringAsync(ct))!;
            query = operations["query"]!.GetValue<string>();
            variables = operations["variables"];
            var map = JsonNode.Parse(await parts["map"].ReadAsStringAsync(ct))!.AsObject();
            var fileField = map.Single().Key;
            file = await parts[fileField].ReadAsByteArrayAsync(ct);
        }
        else
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            query = body["query"]!.GetValue<string>();
            variables = body["variables"];
        }
        Requests.Enqueue(new Captured(query, variables, file, auth, request.RequestUri!.ToString()));

        if (FailNext is { } fail)
        {
            FailNext = null;
            return fail();
        }
        if (auth != $"Bearer {ApiKey}")
            return Json("""{"errors":[{"message":"unauthorized"}],"data":null}""");

        if (query.Contains("createDocument", StringComparison.Ordinal))
            return FailCreateDocument?.Invoke() ?? CreateDocument(variables!);
        if (query.Contains("updateDocument", StringComparison.Ordinal))
            return Json(new JsonObject { ["data"] = new JsonObject { ["updateDocument"] = new JsonObject { ["id"] = variables!["id"]!.GetValue<string>() } } }.ToJsonString());
        if (query.Contains("deleteDocument", StringComparison.Ordinal))
            return Json("""{"data":{"deleteDocument":true}}""");
        if (query.Contains("resendSignatures", StringComparison.Ordinal))
            return Json("""{"data":{"resendSignatures":true}}""");
        if (query.Contains("files", StringComparison.Ordinal))
            return Json(new JsonObject { ["data"] = new JsonObject { ["document"] = new JsonObject { ["files"] = new JsonObject { ["original"] = "https://storage.googleapis.test/o.pdf", ["signed"] = SignedPdfUrl } } } }.ToJsonString());
        if (query.Contains("document(", StringComparison.Ordinal))
            return DocumentStatus(query);
        return Json("""{"errors":[{"message":"unknown_query"}]}""");
    }

    private HttpResponseMessage CreateDocument(JsonNode variables)
    {
        var documentId = Guid.NewGuid().ToString("N") + "a1b2c3d4e5f6a7b8c9"; // como a real: hex sem hífen, 50 caracteres
        var signers = variables["signers"]!.AsArray();
        var ids = new List<string>();
        var signatures = new JsonArray
        {
            // O dono da conta aparece como assinatura sem ação: não pode ser ligado a um signatário.
            new JsonObject { ["public_id"] = Guid.NewGuid().ToString("N"), ["name"] = "Plataforma", ["email"] = "conta@plataforma.test", ["action"] = null, ["link"] = null },
        };
        // Devolve em ordem invertida: o provedor precisa ligar por e-mail/telefone, não pela posição.
        foreach (var signer in signers.Reverse())
        {
            var publicId = Guid.NewGuid().ToString("N"); // a API devolve sem hífen; o webhook manda com
            signatures.Add(new JsonObject
            {
                ["public_id"] = publicId,
                ["name"] = signer!["name"]!.GetValue<string>(),
                ["email"] = signer["email"]?.GetValue<string>(),
                ["action"] = new JsonObject { ["name"] = signer["action"]!.GetValue<string>() },
                ["link"] = new JsonObject { ["short_link"] = $"https://assina.ae/{publicId[..8]}" },
                ["user"] = new JsonObject { ["email"] = signer["email"]?.GetValue<string>(), ["phone"] = signer["phone"]?.GetValue<string>() },
            });
        }
        foreach (var signer in signers)
        {
            var match = signatures.Skip(1).Single(s => s!["name"]!.GetValue<string>() == signer!["name"]!.GetValue<string>());
            ids.Add(match!["public_id"]!.GetValue<string>());
        }
        Documents[documentId] = ids;
        var response = new JsonObject
        {
            ["data"] = new JsonObject { ["createDocument"] = new JsonObject { ["id"] = documentId, ["signatures"] = signatures } },
        };
        return Json(response.ToJsonString());
    }

    private HttpResponseMessage DocumentStatus(string query)
    {
        var documentId = Documents.Keys.FirstOrDefault(id => query.Contains(id, StringComparison.Ordinal));
        if (documentId is null)
            return Json("""{"errors":[{"message":"document_not_found"}],"data":{"document":null}}""");
        static JsonNode? At(DateTimeOffset? d) => d is { } v ? new JsonObject { ["created_at"] = v.ToString("yyyy-MM-dd HH:mm:ss") } : null;
        var signatures = new JsonArray();
        foreach (var publicId in Documents[documentId])
        {
            SignatureState.TryGetValue(publicId, out var state);
            signatures.Add(new JsonObject
            {
                ["public_id"] = publicId,
                ["action"] = new JsonObject { ["name"] = "SIGN" },
                ["viewed"] = At(state.Viewed),
                ["signed"] = At(state.Signed),
                ["rejected"] = null,
                ["email_events"] = new JsonObject { ["refused"] = null, ["reason"] = null },
            });
        }
        return Json(new JsonObject { ["data"] = new JsonObject { ["document"] = new JsonObject { ["id"] = documentId, ["signatures"] = signatures } } }.ToJsonString());
    }

    /// <summary>O mesmo id de assinatura no formato do webhook: UUID com hífens.</summary>
    public static string Dashed(string publicId) => Guid.ParseExact(publicId, "N").ToString("D");

    /// <summary>Webhook no formato da documentação, com o recurso em event.data.object (ou direto em data).</summary>
    public static (string Body, string Signature) Webhook(string type, JsonObject resource, bool resourceUnderObject = true, string? eventId = null)
    {
        var body = new JsonObject
        {
            ["id"] = Convert.ToBase64String("webhook-1"u8),
            ["object"] = "webhook",
            ["name"] = "Contratos",
            ["format"] = "json",
            ["url"] = "https://app.test/api/v1/webhooks/autentique",
            ["event"] = new JsonObject
            {
                ["id"] = eventId ?? Guid.NewGuid().ToString(),
                ["object"] = "event",
                ["organization"] = 123,
                ["type"] = type,
                ["data"] = resourceUnderObject ? new JsonObject { ["object"] = resource } : resource,
                ["created_at"] = "2026-11-15T13:45:10.000000Z",
            },
        }.ToJsonString();
        return (body, Sign(body));
    }

    public static string Sign(string body, string secret = WebhookSecret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

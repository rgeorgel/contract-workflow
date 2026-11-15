using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contratos.Application.Signatures;
using Contratos.Domain.Contracts;
using Contratos.Domain.Templates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Contratos.Infrastructure.Signatures;

public sealed class AutentiqueOptions
{
    public const string Section = "Autentique";

    public string ApiUrl { get; set; } = "https://api.autentique.com.br/v2/graphql";
    /// <summary>Chave da API da conta da plataforma (painel → API). Só em variável de ambiente.</summary>
    public string? ApiKey { get; set; }
    /// <summary>
    /// Secret do endpoint de webhook cadastrado no painel. O painel não deixa um endpoint ouvir documento e
    /// assinatura ao mesmo tempo, então podem ser dois endpoints com secrets diferentes: separe por vírgula.
    /// </summary>
    public string? WebhookSecret { get; set; }

    public IReadOnlyList<string> WebhookSecrets =>
        (WebhookSecret ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    /// <summary>Documentos de teste: não gastam créditos e somem depois de alguns dias.</summary>
    public bool Sandbox { get; set; } = true;
}

/// <summary>Erro devolvido pela Autentique. <see cref="Code"/> é a chave da mensagem (ex.: document_not_found).</summary>
public sealed class AutentiqueException(
    string message, string? code = null, Exception? inner = null,
    IReadOnlyDictionary<string, string[]>? validation = null, bool permanent = false)
    : SignatureProviderException(message, permanent || IsPermanentCode(code), code == "unavailable_credits", inner)
{
    public string? Code { get; } = code;

    /// <summary>Erros de validação por campo, como a Autentique manda (ex.: "signers.0.email": ["format_is_invalid"]).</summary>
    public IReadOnlyDictionary<string, string[]> Validation { get; } = validation ?? new Dictionary<string, string[]>();

    /// <summary>Dados recusados: tentar de novo com os mesmos dados dá o mesmo erro.</summary>
    private static bool IsPermanentCode(string? code) => code is "validation" or "format_is_invalid" or "field_required"
        or "must_be_a_valid_email_address" or "must_be_a_string" or "must_be_an_array" or "not_a_valid_date" or "invalid_date"
        or "must_be_a_file" or "must_be_a_valid_file" or "may_not_be_greater_than" or "must_be_at_least"
        or "sms_delivery_not_allowed_on_foreign_documents";
}

/// <summary>
/// Autentique (API GraphQL v2). Uma conta da plataforma atende todas as organizações; o webhook acha o
/// contrato pelo id do documento. Falhas viram exceção: a outbox tenta de novo com espera.
/// </summary>
internal sealed class AutentiqueSignatureProvider(
    HttpClient http,
    IOptions<AutentiqueOptions> options,
    TimeProvider clock,
    ILogger<AutentiqueSignatureProvider> logger) : ISignatureProvider
{
    public const string ProviderName = "autentique";
    public const string FileField = "file";

    public string Name => ProviderName;

    private AutentiqueOptions Options => options.Value;

    // ---------- Documento ----------

    public async Task<CreatedDocument> CreateDocumentAsync(DocumentRequest req, Stream pdf, CancellationToken ct)
    {
        var args = new List<string>();
        if (Options.Sandbox)
            args.Add("sandbox: true");
        if (!string.IsNullOrWhiteSpace(req.FolderId))
            args.Add($"folder_id: {JsonSerializer.Serialize(req.FolderId)}");
        args.AddRange(["document: $document", "signers: $signers", "file: $file"]);

        var query = $$"""
            mutation CreateDocument($document: DocumentInput!, $signers: [SignerInput!]!, $file: Upload!) {
              createDocument({{string.Join(", ", args)}}) {
                id
                signatures { public_id name email action { name } link { short_link } user { email phone } }
              }
            }
            """;
        var operations = new JsonObject
        {
            ["query"] = query,
            ["variables"] = new JsonObject
            {
                ["document"] = DocumentInput(req),
                ["signers"] = new JsonArray([.. req.Signers.Select(SignerInput)]),
                ["file"] = null,
            },
        };

        // Upload no padrão GraphQL multipart: operations, map e o arquivo, nesta ordem.
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(operations.ToJsonString(), Encoding.UTF8), "operations");
        content.Add(new StringContent($$"""{"{{FileField}}":["variables.file"]}""", Encoding.UTF8), "map");
        var file = new StreamContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, FileField, "contrato.pdf");

        JsonElement data;
        try
        {
            data = await SendAsync(content, ct);
        }
        catch (AutentiqueException e) when (e.Permanent)
        {
            throw new AutentiqueException(Explain(e, req), e.Code, e, e.Validation, permanent: true);
        }
        var document = data.GetProperty("createDocument");
        var documentId = NormalizeId(document.GetProperty("id").GetString()
            ?? throw new AutentiqueException("A Autentique não devolveu o id do documento."));
        return new CreatedDocument(documentId, MatchSignatures(req.Signers, Signatures(document)));
    }

    public async Task DeleteDocumentAsync(string documentId, CancellationToken ct)
    {
        // Excluir não bloqueia um documento já assinado por alguém (ele só vai para a lixeira):
        // antes, o prazo vence agora, o que impede novas assinaturas.
        try
        {
            await QueryAsync("""
                mutation Block($id: UUID!, $document: UpdateDocumentInput!) {
                  updateDocument(id: $id, document: $document) { id }
                }
                """,
                new JsonObject { ["id"] = documentId, ["document"] = new JsonObject { ["deadline_at"] = Iso(clock.GetUtcNow()) } }, ct);
        }
        catch (AutentiqueException e) when (e.Code is "document_not_found" or "document_signed" or "validation")
        {
            logger.LogInformation("Prazo do documento {DocumentId} não alterado antes da exclusão: {Code}", documentId, e.Code);
        }

        try
        {
            await QueryAsync($"mutation {{ deleteDocument(id: {Literal(documentId)}) }}", [], ct);
        }
        catch (AutentiqueException e) when (e.Code == "document_not_found")
        {
            // Já não existe lá: o objetivo foi atingido.
        }
    }

    public async Task ResendAsync(IReadOnlyList<string> signaturePublicIds, CancellationToken ct)
    {
        try
        {
            var ids = string.Join(", ", signaturePublicIds.Select(Literal));
            await QueryAsync($"mutation {{ resendSignatures(public_ids: [{ids}]) }}", [], ct);
        }
        catch (AutentiqueException e) when (e.Code == "too_many_resent_emails")
        {
            // A Autentique limita reenvios em sequência; o link enviado há pouco continua valendo.
            logger.LogInformation("Reenvio ignorado pela Autentique (enviado há pouco)");
        }
    }

    public async Task<DocumentStatus> GetDocumentAsync(string documentId, CancellationToken ct)
    {
        var data = await QueryAsync($$"""
            query {
              document(id: {{Literal(documentId)}}) {
                id
                signatures {
                  public_id action { name }
                  viewed { created_at } signed { created_at } rejected { created_at reason }
                  email_events { refused reason }
                }
              }
            }
            """, [], ct);

        var signatures = Signatures(data.GetProperty("document")).Where(s => s.Action is not null).ToList();
        var events = new List<SignatureEvent>();
        foreach (var s in signatures)
        {
            // Ids estáveis: a reconciliação pode rodar várias vezes; o domínio ignora o repetido.
            string Id(string kind) => $"reconcile:{documentId}:{s.PublicId}:{kind}";
            if (s.ViewedAt is { } viewed) events.Add(new(Id("viewed"), SignatureEventType.Viewed, documentId, s.PublicId, null, viewed));
            if (s.SignedAt is { } signed) events.Add(new(Id("signed"), SignatureEventType.Signed, documentId, s.PublicId, null, signed));
            if (s.RejectedAt is { } rejected) events.Add(new(Id("rejected"), SignatureEventType.Declined, documentId, s.PublicId, s.RejectReason, rejected));
            if (s.EmailRefusedAt is { } refused && s.SignedAt is null && s.ViewedAt is null)
                events.Add(new(Id("refused"), SignatureEventType.DeliveryFailed, documentId, s.PublicId, null, refused));
        }

        var finished = signatures.Count > 0 && signatures.All(s => s.SignedAt is not null);
        if (finished)
            events.Add(new($"reconcile:{documentId}:finished", SignatureEventType.DocumentFinished, documentId, null, null,
                signatures.Max(s => s.SignedAt!.Value)));
        return new DocumentStatus(documentId, finished, events);
    }

    public async Task<Stream> DownloadSignedPdfAsync(string documentId, CancellationToken ct)
    {
        var data = await QueryAsync($"query {{ document(id: {Literal(documentId)}) {{ files {{ signed }} }} }}", [], ct);
        var url = data.GetProperty("document").GetProperty("files").TryGetProperty("signed", out var signed) ? signed.GetString() : null;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new AutentiqueException("O PDF assinado ainda não está disponível na Autentique.", "signed_file_unavailable");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        // O token só vai para a própria Autentique; links de storage (ex.: Google) são assinados na URL.
        if (IsAutentiqueHost(uri))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.ApiKey);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new AutentiqueException($"Download do PDF assinado falhou ({(int)response.StatusCode}).", "download_failed");

        var buffer = new MemoryStream();
        await response.Content.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        if (buffer.Length < 5 || Encoding.ASCII.GetString(buffer.GetBuffer(), 0, 5) != "%PDF-")
            throw new AutentiqueException("O arquivo assinado baixado da Autentique não é um PDF.", "download_failed");
        return buffer;
    }

    public async Task<ProviderBalance?> GetBalanceAsync(CancellationToken ct)
    {
        var data = await QueryAsync("query { me { subscription { documents credits } } }", [], ct);
        if (!data.TryGetProperty("me", out var me) || !me.TryGetProperty("subscription", out var sub) || sub.ValueKind != JsonValueKind.Object)
            return new ProviderBalance(null, null);
        static int? Int(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
        return new ProviderBalance(Int(sub, "documents"), Int(sub, "credits"));
    }

    // ---------- Mensagens para o usuário ----------

    /// <summary>
    /// "signers.1.email: format_is_invalid" vira "A Autentique recusou o e-mail de Carol (formato inválido)."
    /// O índice é a posição em <see cref="DocumentRequest.Signers"/> (a ordem de envio).
    /// </summary>
    internal static string Explain(AutentiqueException e, DocumentRequest req)
    {
        const string foreignSms = "sms_delivery_not_allowed_on_foreign_documents";
        if (e.Code == foreignSms || e.Validation.Values.Any(codes => codes.Any(c => c.StartsWith(foreignSms, StringComparison.Ordinal))))
            return "A Autentique só envia SMS para telefones do Brasil. Troque o canal desse signatário para WhatsApp ou e-mail e envie de novo.";

        var parts = new List<string>();
        foreach (var (field, codes) in e.Validation)
        {
            var reason = string.Join(", ", codes.Select(Reason).Distinct());
            var segments = field.Split('.');
            if (segments is ["signers", var index, ..] && int.TryParse(index, out var i) && i >= 0 && i < req.Signers.Count)
                parts.Add($"{Field(segments[^1])} de {req.Signers[i].Name} ({reason})");
            else
                parts.Add($"{Field(segments[^1])} ({reason})");
        }
        if (parts.Count == 0)
            parts.Add(e.Code is { } c ? Reason(c) : "dados inválidos");
        return $"A Autentique recusou {string.Join("; ", parts)}. Volte o contrato para rascunho, corrija e envie de novo.";
    }

    private static string Field(string name) => name switch
    {
        "email" => "o e-mail",
        "phone" => "o telefone",
        "name" => "o nome",
        "cpf" => "o CPF",
        "delivery_method" => "o canal de envio",
        "file" => "o PDF",
        "deadline_at" => "a data limite",
        "message" => "a mensagem",
        _ => $"o campo {name}",
    };

    private static string Reason(string code) => code.Split(':')[0] switch
    {
        "format_is_invalid" or "must_be_a_valid_email_address" => "formato inválido",
        "field_required" => "obrigatório",
        "may_not_be_greater_than" => "longo demais",
        "must_be_at_least" => "curto demais",
        "not_a_valid_date" or "invalid_date" => "data inválida",
        "must_be_a_valid_file" or "must_be_a_file" => "arquivo inválido",
        "validation" => "dados inválidos",
        var other => other,
    };

    // ---------- Webhook ----------

    public bool ValidateWebhook(string rawBody, string? signatureHeader)
    {
        var secrets = Options.WebhookSecrets;
        if (secrets.Count == 0 || string.IsNullOrWhiteSpace(signatureHeader))
            return false;
        var received = signatureHeader.Trim();
        if (received.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            received = received["sha256=".Length..];
        var receivedBytes = Encoding.ASCII.GetBytes(received.ToLowerInvariant());
        var body = Encoding.UTF8.GetBytes(rawBody);

        var valid = false;
        foreach (var secret in secrets) // todos, sem parar no primeiro: o tempo não revela qual bateu
        {
            var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
            valid |= CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), receivedBytes);
        }
        return valid;
    }

    /// <summary>
    /// Corpo: { id, object: "webhook", event: { id, type, data, created_at } }. A documentação mostra o recurso
    /// às vezes em event.data.object e às vezes direto em event.data: os dois formatos são aceitos.
    /// Eventos que o sistema não usa voltam como lista vazia.
    /// </summary>
    public IReadOnlyList<SignatureEvent> ParseWebhook(string rawBody)
    {
        using var json = JsonDocument.Parse(rawBody);
        var root = json.RootElement;
        var ev = root.TryGetProperty("event", out var inner) && inner.ValueKind == JsonValueKind.Object ? inner : root;
        var eventId = Str(ev, "id") ?? throw new FormatException("Webhook sem event.id.");
        var type = Str(ev, "type") ?? throw new FormatException("Webhook sem event.type.");
        if (!ev.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new FormatException("Webhook sem event.data.");
        var resource = data.TryGetProperty("object", out var obj) && obj.ValueKind == JsonValueKind.Object ? obj : data;
        var occurredAt = Date(ev, "created_at") ?? clock.GetUtcNow();

        SignatureEvent Signature(SignatureEventType kind, string? reason = null) => new(
            eventId, kind, NormalizeId(SignatureDocumentId(resource)),
            NormalizeId(Str(resource, "public_id") ?? throw new FormatException("Evento de assinatura sem public_id.")),
            reason, occurredAt);
        SignatureEvent Document(SignatureEventType kind) => new(
            eventId, kind, NormalizeId(Str(resource, "id") ?? throw new FormatException("Evento de documento sem id.")), null, null, occurredAt);

        return type switch
        {
            "signature.viewed" => [Signature(SignatureEventType.Viewed)],
            "signature.accepted" => [Signature(SignatureEventType.Signed)],
            "signature.rejected" => [Signature(SignatureEventType.Declined, RejectReason(resource))],
            "signature.delivery_failed" => [Signature(SignatureEventType.DeliveryFailed)],
            "document.finished" => [Document(SignatureEventType.DocumentFinished)],
            "document.deleted" => [Document(SignatureEventType.DocumentDeleted)],
            _ => [],
        };
    }

    private static string SignatureDocumentId(JsonElement signature) =>
        signature.TryGetProperty("document", out var doc)
            ? doc.ValueKind switch
            {
                JsonValueKind.String => doc.GetString() ?? "",
                JsonValueKind.Object => Str(doc, "id") ?? "",
                _ => "",
            }
            : ""; // sem o documento: o processador acha o contrato pelo public_id da assinatura

    private static string? RejectReason(JsonElement signature) =>
        signature.TryGetProperty("rejected", out var rejected) && rejected.ValueKind == JsonValueKind.Object
            ? Str(rejected, "reason")
            : Str(signature, "reason");

    // ---------- Mapeamento ----------

    private static JsonObject DocumentInput(DocumentRequest req)
    {
        var doc = new JsonObject
        {
            ["name"] = req.Name.Length > 200 ? req.Name[..200] : req.Name,
            ["sortable"] = req.Sortable,
            ["refusable"] = true,
            ["stop_on_rejected"] = true,
            ["locale"] = new JsonObject
            {
                ["country"] = "BR",
                ["language"] = "pt-BR",
                ["timezone"] = "America/Sao_Paulo",
                ["date_format"] = "DD_MM_YYYY",
            },
        };
        if (!string.IsNullOrWhiteSpace(req.Message))
            doc["message"] = req.Message;
        if (req.Reminder is "DAILY" or "WEEKLY")
            doc["reminder"] = req.Reminder;
        if (req.DeadlineAt is { } deadline)
            doc["deadline_at"] = Iso(deadline);
        return doc;
    }

    private static JsonObject SignerInput(DocumentSigner s)
    {
        var signer = new JsonObject { ["name"] = s.Name, ["action"] = Action(s.Action) };
        switch (s.DeliveryMethod)
        {
            case DeliveryMethod.WhatsApp:
                signer["phone"] = s.Phone;
                signer["delivery_method"] = "DELIVERY_METHOD_WHATSAPP";
                break;
            case DeliveryMethod.Sms:
                signer["phone"] = s.Phone;
                signer["delivery_method"] = "DELIVERY_METHOD_SMS";
                break;
            default:
                signer["email"] = s.Email;
                break;
        }
        var cpf = new string((s.Cpf ?? "").Where(char.IsAsciiDigit).ToArray());
        if (cpf.Length == 11)
            signer["configs"] = new JsonObject { ["cpf"] = cpf };
        return signer;
    }

    internal static string Action(SignerAction action) => action switch
    {
        SignerAction.SignAsWitness => "SIGN_AS_A_WITNESS",
        SignerAction.Approve => "APPROVE",
        SignerAction.Recognize => "RECOGNIZE",
        _ => "SIGN",
    };

    private sealed record ReturnedSignature(
        string PublicId, string? Name, string? Email, string? Phone, string? Action, string? ShortLink,
        DateTimeOffset? ViewedAt, DateTimeOffset? SignedAt, DateTimeOffset? RejectedAt, string? RejectReason, DateTimeOffset? EmailRefusedAt);

    private static List<ReturnedSignature> Signatures(JsonElement document)
    {
        var list = new List<ReturnedSignature>();
        if (!document.TryGetProperty("signatures", out var signatures) || signatures.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var s in signatures.EnumerateArray())
        {
            if (Str(s, "public_id") is not { } rawId)
                continue;
            var publicId = NormalizeId(rawId);
            var user = s.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;
            list.Add(new ReturnedSignature(
                publicId,
                Str(s, "name"),
                Str(s, "email") ?? (user.ValueKind == JsonValueKind.Object ? Str(user, "email") : null),
                user.ValueKind == JsonValueKind.Object ? Str(user, "phone") : null,
                s.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.Object ? Str(a, "name") : null,
                s.TryGetProperty("link", out var l) && l.ValueKind == JsonValueKind.Object ? Str(l, "short_link") : null,
                EventDate(s, "viewed"), EventDate(s, "signed"), EventDate(s, "rejected"),
                s.TryGetProperty("rejected", out var r) && r.ValueKind == JsonValueKind.Object ? Str(r, "reason") : null,
                s.TryGetProperty("email_events", out var ee) && ee.ValueKind == JsonValueKind.Object ? Date(ee, "refused") : null));
        }
        return list;
    }

    /// <summary>
    /// Liga cada signatário pedido à assinatura devolvida: por e-mail, telefone ou nome; o que sobrar, pela ordem.
    /// Assinaturas sem ação (o dono do documento, quando aparece) ficam de fora.
    /// </summary>
    private static List<CreatedSignature> MatchSignatures(IReadOnlyList<DocumentSigner> requested, List<ReturnedSignature> returned)
    {
        var pool = returned.Where(s => s.Action is not null).ToList();
        if (pool.Count < requested.Count)
            pool = returned.ToList();
        if (pool.Count < requested.Count)
            throw new AutentiqueException($"A Autentique devolveu {pool.Count} assinaturas para {requested.Count} signatários.");

        var result = new ReturnedSignature?[requested.Count];
        for (var i = 0; i < requested.Count; i++)
        {
            var want = requested[i];
            var match = pool.FirstOrDefault(s =>
                (want.Email is { Length: > 0 } email && string.Equals(s.Email, email, StringComparison.OrdinalIgnoreCase))
                || (want.Phone is { Length: > 0 } phone && Digits(s.Phone) is { Length: > 0 } p && Digits(phone).EndsWith(p, StringComparison.Ordinal)));
            if (match is not null)
            {
                result[i] = match;
                pool.Remove(match);
            }
        }
        for (var i = 0; i < requested.Count; i++)
        {
            if (result[i] is not null)
                continue;
            var byName = pool.FirstOrDefault(s => string.Equals(s.Name, requested[i].Name, StringComparison.OrdinalIgnoreCase));
            result[i] = byName ?? pool[0];
            pool.Remove(result[i]!);
        }
        return result.Select(s => new CreatedSignature(s!.PublicId, s.ShortLink)).ToList();
    }

    // ---------- HTTP ----------

    private Task<JsonElement> QueryAsync(string query, JsonObject variables, CancellationToken ct)
    {
        var body = new JsonObject { ["query"] = query, ["variables"] = variables };
        return SendAsync(new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct);
    }

    private async Task<JsonElement> SendAsync(HttpContent content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Options.ApiUrl) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException e)
        {
            throw new AutentiqueException("Não foi possível falar com a Autentique.", "network", e);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new AutentiqueException("Limite de requisições da Autentique atingido (60 por minuto).", "rate_limited");
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new AutentiqueException("A Autentique recusou a chave da API.", "unauthorized");

            var text = await response.Content.ReadAsStringAsync(ct);
            JsonDocument json;
            try
            {
                json = JsonDocument.Parse(text);
            }
            catch (JsonException e)
            {
                throw new AutentiqueException($"Resposta inválida da Autentique ({(int)response.StatusCode}).", "invalid_response", e);
            }

            using (json)
            {
                var root = json.RootElement;
                if (root.TryGetProperty("errors", out var errors) && Error(errors) is { } error)
                    throw error;
                if (!response.IsSuccessStatusCode)
                    throw new AutentiqueException($"A Autentique respondeu {(int)response.StatusCode}.", "http_" + (int)response.StatusCode);
                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                    throw new AutentiqueException("Resposta da Autentique sem dados.", "invalid_response");
                return data.Clone();
            }
        }
    }

    /// <summary>Erros GraphQL: a mensagem é a chave (document_not_found, unavailable_credits...); validação traz os campos.</summary>
    private static AutentiqueException? Error(JsonElement errors)
    {
        var items = errors.ValueKind switch
        {
            JsonValueKind.Array => errors.EnumerateArray().ToList(),
            JsonValueKind.Object => [errors],
            _ => [],
        };
        if (items.Count == 0)
            return null;

        var first = items[0];
        var code = Str(first, "message") ?? "unknown";
        var details = new List<string>();
        var fields = new Dictionary<string, string[]>();
        if (first.TryGetProperty("extensions", out var ext) && ext.TryGetProperty("validation", out var validation)
            && validation.ValueKind == JsonValueKind.Object)
        {
            foreach (var field in validation.EnumerateObject())
            {
                var codes = field.Value.ValueKind == JsonValueKind.Array
                    ? field.Value.EnumerateArray().Select(v => v.ToString()).ToArray()
                    : [field.Value.ToString()];
                fields[field.Name] = codes;
                details.Add($"{field.Name}: {string.Join(", ", codes)}");
            }
        }
        var message = code switch
        {
            "unavailable_credits" => "A conta da Autentique está sem créditos de documentos.",
            "unauthorized" => "A Autentique recusou a chave da API.",
            _ => $"Autentique: {code}",
        };
        if (details.Count > 0)
            message += $" ({string.Join("; ", details)})";
        return new AutentiqueException(message, code, validation: fields);
    }

    private static bool IsAutentiqueHost(Uri uri) =>
        uri.Host.Equals("autentique.com.br", StringComparison.OrdinalIgnoreCase)
        || uri.Host.EndsWith(".autentique.com.br", StringComparison.OrdinalIgnoreCase);

    private static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// A API devolve ids sem hífen ("9322bc10e0a711f1…") e o webhook manda os mesmos ids como UUID com hífen
    /// ("9322bc10-e0a7-11f1-…"). Tudo o que é guardado ou comparado passa por aqui.
    /// </summary>
    internal static string NormalizeId(string id) => id.Replace("-", "", StringComparison.Ordinal).Trim().ToLowerInvariant();

    /// <summary>String GraphQL com escape (a sintaxe de string JSON vale em GraphQL).</summary>
    private static string Literal(string value) => JsonSerializer.Serialize(value);

    private static string Digits(string? value) => new((value ?? "").Where(char.IsAsciiDigit).ToArray());

    private static string? Str(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? v.ToString()
            : null;

    private static DateTimeOffset? Date(JsonElement el, string name) =>
        Str(el, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;

    private static DateTimeOffset? EventDate(JsonElement signature, string name) =>
        signature.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Object ? Date(e, "created_at") : null;
}

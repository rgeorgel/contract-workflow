using System.Text.Json;
using Contratos.Api.Security;
using Contratos.Application.Common;
using Contratos.Application.Contracts;
using Contratos.Application.Integrations;
using Contratos.Application.Templates;
using Contratos.Domain.Contracts;
using Contratos.Infrastructure.Integrations;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Api.Endpoints;

/// <summary>
/// API pública (<c>/api/public/v1</c>), para o sistema do cliente (CRM, ERP) criar e acompanhar contratos.
/// Autentica por chave de API, sem cookie e sem antiforgery. JSON com enums em snake_case (<see cref="PublicJson"/>).
/// A referência está em <c>wwwroot/desenvolvedores</c>; o teste de rotas confere que as duas batem.
/// </summary>
internal static class PublicApiEndpoints
{
    public const string Prefix = "/api/public/v1";
    /// <summary>Um contrato com 30 signatários e as variáveis cabe com folga.</summary>
    public const int MaxBodyBytes = 256 * 1024;

    public static void MapPublicApiEndpoints(this WebApplication app)
    {
        var g = app.MapGroup(Prefix)
            .WithTags("API pública")
            .ExcludeFromDescription()
            .RequireAuthorization(Policies.PublicApi)
            .AddEndpointFilter(RequirePlanAsync);

        g.MapGet("/account", async (IIntegrationService integrations, CancellationToken ct) =>
            Json(await integrations.GetAccountAsync(ct)));

        // ---------- Templates ----------

        g.MapGet("/templates", async (ITemplateService templates, CancellationToken ct) =>
            Json((await templates.ListAsync(false, ct))
                .Where(t => t.CurrentVersion is not null)
                .Select(t => new PublicTemplateListItem(t.Id, t.Name, t.Description, t.CurrentVersion!.Value, t.VariableCount, t.UpdatedAt))
                .ToList()));

        g.MapGet("/templates/{id:guid}", async (Guid id, ITemplateService templates, CancellationToken ct) =>
        {
            var result = await templates.GetAsync(id, ct);
            if (!result.IsSuccess)
                return result.Error!.ToHttp();
            var t = result.Value!;
            if (t.Current is not { } v || t.ArchivedAt is not null)
                return ResultHttp.Problem(StatusCodes.Status404NotFound, "Este template não tem versão publicada ativa.", "not_found");
            return Json(new PublicTemplate(t.Id, t.Name, t.Description, v.Version,
                v.Variables.Select(x => new PublicVariable(x.Key, x.Label, x.Type, x.Required)).ToList(),
                v.DefaultSignerRoles.OrderBy(r => r.Order).Select(r => new PublicSignerRole(r.Role, r.Action, r.Order)).ToList(),
                t.UpdatedAt));
        });

        // ---------- Contratos ----------

        g.MapGet("/contracts", async (HttpRequest http, IContractService contracts, CancellationToken ct) =>
        {
            var q = http.Query;
            ContractStatus? status = null;
            if (q["status"].ToString() is { Length: > 0 } rawStatus)
            {
                if (ParseEnum<ContractStatus>(rawStatus) is not { } parsed)
                    return Invalid("status", $"Use um destes: {string.Join(", ", Enum.GetNames<ContractStatus>().Select(PublicJson.Snake))}.");
                status = parsed;
            }
            Guid? templateId = null;
            if (q["templateId"].ToString() is { Length: > 0 } rawTemplate)
            {
                if (!Guid.TryParse(rawTemplate, out var tid))
                    return Invalid("templateId", "Informe o id do template.");
                templateId = tid;
            }
            DateOnly? from = null, to = null;
            if (q["createdFrom"].ToString() is { Length: > 0 } rawFrom)
            {
                if (!DateOnly.TryParseExact(rawFrom, "yyyy-MM-dd", out var d))
                    return Invalid("createdFrom", "Use o formato AAAA-MM-DD.");
                from = d;
            }
            if (q["createdTo"].ToString() is { Length: > 0 } rawTo)
            {
                if (!DateOnly.TryParseExact(rawTo, "yyyy-MM-dd", out var d))
                    return Invalid("createdTo", "Use o formato AAAA-MM-DD.");
                to = d;
            }
            var page = int.TryParse(q["page"], out var p) ? Math.Max(1, p) : 1;
            var pageSize = int.TryParse(q["pageSize"], out var ps) ? Math.Clamp(ps, 1, 100) : 20;
            var reference = q["externalReference"].ToString();

            var list = await contracts.ListAsync(new ContractListQuery(
                q["q"].ToString() is { Length: > 0 } text ? text : null, status, templateId, from, to, page, pageSize,
                "created_asc".Equals(q["sort"], StringComparison.Ordinal) ? "created_asc" : null, null, null,
                string.IsNullOrWhiteSpace(reference) ? null : reference), ct);
            return Json(new PublicPage<PublicContractListItem>(
                list.Items.Select(PublicMapper.ListItem).ToList(), list.Page, list.PageSize, list.Total, list.Page * list.PageSize < list.Total));
        });

        g.MapPost("/contracts", async (HttpRequest http, IContractService contracts, CancellationToken ct) =>
        {
            var (body, invalid) = await ReadAsync<PublicCreateContractRequest>(http, ct);
            if (invalid is not null)
                return invalid;
            var key = IdempotencyKey(http);
            var row = new BulkContractRow(body!.Title, body.Variables, body.ExpiresAt, body.Message, body.SigningMode,
                body.Signers?.Select(s => new SignerRequest(s.Name, s.Email, s.Phone, s.Cpf, s.DeliveryMethod, s.Role, s.Action, s.SignOrder)).ToList());
            var created = await contracts.CreateCompleteAsync(
                new CompleteContractRequest(body.TemplateId ?? Guid.Empty, row, body.ExternalReference), key, ct);
            if (!created.IsSuccess)
                return PublicError(created.Error!);

            var (detail, replayed) = (created.Value!.Contract, created.Value.Replayed);
            if (body.Send)
            {
                // Mesma chave no envio: repetir a chamada inteira não envia duas vezes.
                var sent = await contracts.SendAsync(detail.Id, key, ct);
                if (!sent.IsSuccess)
                    return PublicError(sent.Error!, detail.Id);
                detail = sent.Value!;
            }
            var location = $"{Prefix}/contracts/{detail.Id}";
            http.HttpContext.Response.Headers.Location = location;
            if (replayed)
                http.HttpContext.Response.Headers["Idempotent-Replayed"] = "true";
            return Json(PublicMapper.Contract(detail), replayed ? StatusCodes.Status200OK : StatusCodes.Status201Created);
        })
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxBodyBytes));

        g.MapGet("/contracts/{id:guid}", async (Guid id, IContractService contracts, CancellationToken ct) =>
            Contract(await contracts.GetAsync(id, ct)));

        g.MapPost("/contracts/{id:guid}/send", async (Guid id, HttpRequest http, IContractService contracts, CancellationToken ct) =>
            Contract(await contracts.SendAsync(id, IdempotencyKey(http), ct), StatusCodes.Status202Accepted));

        g.MapPost("/contracts/{id:guid}/cancel", async (Guid id, IContractService contracts, CancellationToken ct) =>
            Contract(await contracts.CancelAsync(id, ct)));

        g.MapPost("/contracts/{id:guid}/signers/{signerId:guid}/resend", async (Guid id, Guid signerId, IContractService contracts, CancellationToken ct) =>
            Contract(await contracts.ResendAsync(id, signerId, ct)));

        g.MapGet("/contracts/{id:guid}/events", async (Guid id, IContractService contracts, CancellationToken ct) =>
        {
            var result = await contracts.GetEventsAsync(id, ct);
            return result.IsSuccess
                ? Json(result.Value!.Select(e => new PublicEvent(e.Id, PublicJson.Snake(e.Type), e.OccurredAt, e.SignerName, e.ActorName, e.Payload)).ToList())
                : PublicError(result.Error!);
        });

        g.MapGet("/contracts/{id:guid}/document", (Guid id, IContractService contracts, CancellationToken ct) => File(contracts, id, false, ct));
        g.MapGet("/contracts/{id:guid}/signed-document", (Guid id, IContractService contracts, CancellationToken ct) => File(contracts, id, true, ct));

        // ---------- Embed ----------

        g.MapPost("/embed/sessions", async (HttpRequest http, EmbedSessions sessions, CancellationToken ct) =>
        {
            // Corpo vazio: todas as permissões, 60 minutos.
            PublicCreateEmbedSessionRequest? body = new(null, null, null, null, null);
            if (http.ContentLength > 0 || http.HasJsonContentType())
            {
                (body, var invalid) = await ReadAsync<PublicCreateEmbedSessionRequest>(http, ct);
                if (invalid is not null)
                    return invalid;
            }
            var keyId = Guid.Parse(http.HttpContext.User.FindFirst(ApiKeyAuthenticationHandler.KeyIdClaim)!.Value);
            var created = await sessions.CreateAsync(keyId, body!, ct);
            if (!created.IsSuccess)
                return PublicError(created.Error!);
            http.HttpContext.Response.Headers.Location = $"{Prefix}/embed/sessions/{created.Value!.Id}";
            return Json(created.Value, StatusCodes.Status201Created);
        });

        g.MapDelete("/embed/sessions/{id:guid}", async (Guid id, EmbedSessions sessions, CancellationToken ct) =>
        {
            var result = await sessions.RevokeAsync(id, ct);
            return result.IsSuccess ? TypedResults.NoContent() : PublicError(result.Error!);
        });

        // Qualquer outra rota sob o prefixo: 404 em JSON, não a página do site.
        g.MapFallback(() => ResultHttp.Problem(StatusCodes.Status404NotFound, "Rota inexistente na API pública. Confira a documentação.", "route_not_found"));
    }

    /// <summary>Chave válida, mas o plano atual não inclui a API (mudou de plano depois de criar a chave).</summary>
    internal static async ValueTask<object?> RequirePlanAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var tenant = http.RequestServices.GetRequiredService<ICurrentTenant>();
        var db = http.RequestServices.GetRequiredService<AppDbContext>();
        var planId = await db.Organizations.Where(o => o.Id == tenant.RequireOrganizationId()).Select(o => o.PlanId).SingleAsync(http.RequestAborted);
        return Domain.Billing.Plans.Get(planId).ApiAccess ? await next(ctx) : IntegrationErrors.NotInPlan().ToHttp();
    }

    // ---------- Apoio ----------

    private static IResult Json<T>(T value, int status = StatusCodes.Status200OK) =>
        TypedResults.Json(value, PublicJson.Options, statusCode: status);

    private static IResult Contract(Result<ContractDetail> result, int status = StatusCodes.Status200OK) =>
        result.IsSuccess ? Json(PublicMapper.Contract(result.Value!), status) : PublicError(result.Error!);

    private static async Task<IResult> File(IContractService contracts, Guid id, bool signed, CancellationToken ct)
    {
        var result = await contracts.GetDocumentAsync(id, signed, ct);
        return result.IsSuccess ? TypedResults.Stream(result.Value!.Content, "application/pdf", result.Value.FileName) : PublicError(result.Error!);
    }

    private static string? IdempotencyKey(HttpRequest http)
    {
        var key = http.Headers["Idempotency-Key"].ToString().Trim();
        return key.Length == 0 ? null : key[..Math.Min(key.Length, 100)];
    }

    private static IResult Invalid(string field, string message) => Error.Validation(field, message).ToHttp();

    /// <summary>Os nomes dos campos com erro seguem o corpo da API pública (<c>variables.x</c>, não <c>variableValues.x</c>).</summary>
    private static IResult PublicError(Error e, Guid? contractId = null)
    {
        if (e.Kind == ErrorKind.Validation && e.Fields is { IsEmpty: false } fields)
        {
            var renamed = new ValidationErrors();
            foreach (var (field, messages) in fields.ToDictionary())
                foreach (var message in messages)
                    renamed.Add(field.StartsWith("variableValues", StringComparison.Ordinal) ? "variables" + field["variableValues".Length..] : field, message);
            e = e with { Fields = renamed };
        }
        if (contractId is null)
            return e.ToHttp();
        // Criado, mas não enviado (sem cota, por exemplo): o cliente sabe qual rascunho enviar depois.
        var http = e.ToHttp();
        return http is Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult problem
            ? TypedResults.Problem(problem.ProblemDetails.Detail, null, problem.StatusCode, problem.ProblemDetails.Title, null,
                new Dictionary<string, object?>(problem.ProblemDetails.Extensions) { ["contractId"] = contractId })
            : http;
    }

    private static async Task<(T? Value, IResult? Error)> ReadAsync<T>(HttpRequest request, CancellationToken ct) where T : class
    {
        // O Kestrel também barra pelo metadado da rota; aqui a resposta sai em JSON, com código.
        if (request.ContentLength > MaxBodyBytes)
            return (null, TooLarge());
        if (!request.HasJsonContentType())
            return (null, ResultHttp.Problem(StatusCodes.Status415UnsupportedMediaType,
                "Envie o corpo em JSON, com o cabeçalho Content-Type: application/json.", "unsupported_media_type"));
        try
        {
            var value = await JsonSerializer.DeserializeAsync<T>(request.Body, PublicJson.Options, ct);
            return value is null
                ? (null, ResultHttp.Problem(StatusCodes.Status400BadRequest, "O corpo da requisição está vazio.", "invalid_json"))
                : (value, null);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, TooLarge());
        }
        catch (JsonException e)
        {
            var where = string.IsNullOrEmpty(e.Path) || e.Path == "$" ? "" : $" Confira o campo {e.Path.TrimStart('$', '.')}.";
            return (null, ResultHttp.Problem(StatusCodes.Status400BadRequest, $"JSON inválido.{where}", "invalid_json"));
        }
    }

    private static IResult TooLarge() => ResultHttp.Problem(StatusCodes.Status413PayloadTooLarge,
        $"O corpo da requisição passou do limite de {MaxBodyBytes / 1024} KB.", "payload_too_large");

    private static T? ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.GetValues<T>().Cast<T?>().FirstOrDefault(v => PublicJson.Snake(v!.Value.ToString()) == value.Trim().ToLowerInvariant());
}

using Contratos.Api.Security;
using Contratos.Application.Templates;

namespace Contratos.Api.Endpoints;

internal static class TemplatesEndpoints
{
    /// <summary>CSP da pré-visualização: o HTML do template usa estilos inline; nada de script nem rede.</summary>
    public const string PreviewCsp = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; frame-ancestors 'self'";

    public static RouteGroupBuilder MapTemplatesEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/templates").WithTags("Templates").RequireAuthorization();

        g.MapGet("/", async (bool? archived, ITemplateService templates, CancellationToken ct) =>
                TypedResults.Ok(await templates.ListAsync(archived ?? false, ct)))
            .WithSummary("Lista os templates (ativos ou arquivados)");

        g.MapPost("/", async (CreateTemplateRequest req, ITemplateService templates, HttpContext http, CancellationToken ct) =>
                (await templates.CreateAsync(req, ct)).ToHttp(t => WithETag(http, t, TypedResults.Created($"/api/v1/templates/{t.Id}", t))))
            .RequireAuthorization(Policies.IsAdmin)
            .WithSummary("Cria um template com rascunho vazio");

        g.MapGet("/{id:guid}", async (Guid id, ITemplateService templates, HttpContext http, CancellationToken ct) =>
                (await templates.GetAsync(id, ct)).ToHttp(t => WithETag(http, t, TypedResults.Ok(t))))
            .WithSummary("Detalhe: versão publicada, rascunho e histórico");

        g.MapPut("/{id:guid}", async (Guid id, SaveTemplateRequest req, ITemplateService templates, HttpContext http, CancellationToken ct) =>
                (await templates.SaveDraftAsync(id, req, ETags.IfMatch(http), ct)).ToHttp(t => WithETag(http, t, TypedResults.Ok(t))))
            .RequireAuthorization(Policies.IsAdmin)
            .WithSummary("Salva o rascunho da próxima versão (If-Match opcional)");

        g.MapPost("/{id:guid}/publish", Admin((templates, id, ct) => templates.PublishAsync(id, ct)))
            .RequireAuthorization(Policies.IsAdmin).WithSummary("Publica o rascunho como nova versão");
        g.MapDelete("/{id:guid}/draft", Admin((templates, id, ct) => templates.DiscardDraftAsync(id, ct)))
            .RequireAuthorization(Policies.IsAdmin).WithSummary("Descarta o rascunho");
        g.MapPost("/{id:guid}/duplicate", async (Guid id, ITemplateService templates, HttpContext http, CancellationToken ct) =>
                (await templates.DuplicateAsync(id, ct)).ToHttp(t => WithETag(http, t, TypedResults.Created($"/api/v1/templates/{t.Id}", t))))
            .RequireAuthorization(Policies.IsAdmin).WithSummary("Duplica o template");
        g.MapPost("/{id:guid}/archive", Admin((templates, id, ct) => templates.SetArchivedAsync(id, true, ct)))
            .RequireAuthorization(Policies.IsAdmin).WithSummary("Arquiva");
        g.MapPost("/{id:guid}/unarchive", Admin((templates, id, ct) => templates.SetArchivedAsync(id, false, ct)))
            .RequireAuthorization(Policies.IsAdmin).WithSummary("Desarquiva");

        g.MapGet("/{id:guid}/versions/{versionId:guid}", async (Guid id, Guid versionId, ITemplateService templates, CancellationToken ct) =>
                (await templates.GetVersionAsync(id, versionId, ct)).ToHttp())
            .WithSummary("Conteúdo de uma versão");

        g.MapGet("/{id:guid}/preview", async (Guid id, string? source, ITemplateService templates, HttpContext http, CancellationToken ct) =>
            {
                var which = string.Equals(source, "current", StringComparison.OrdinalIgnoreCase) ? PreviewSource.Current : PreviewSource.Draft;
                var result = await templates.PreviewAsync(id, which, ct);
                return result.ToHttp(html => Html(http, html));
            })
            .WithSummary("HTML com dados de exemplo, para o iframe de pré-visualização");

        return api;
    }

    /// <summary>Página HTML gerada (pré-visualização) com CSP própria e permissão de iframe na mesma origem.</summary>
    public static IResult Html(HttpContext http, string html)
    {
        http.Response.Headers.ContentSecurityPolicy = PreviewCsp;
        http.Response.Headers.XFrameOptions = "SAMEORIGIN";
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Content(html, "text/html; charset=utf-8");
    }

    private static Func<Guid, ITemplateService, HttpContext, CancellationToken, Task<IResult>> Admin(
        Func<ITemplateService, Guid, CancellationToken, Task<Contratos.Application.Common.Result<TemplateDetail>>> action) =>
        async (id, templates, http, ct) => (await action(templates, id, ct)).ToHttp(t => WithETag(http, t, TypedResults.Ok(t)));

    private static IResult WithETag(HttpContext http, TemplateDetail t, IResult result)
    {
        ETags.Set(http, t.RowVersion);
        return result;
    }
}

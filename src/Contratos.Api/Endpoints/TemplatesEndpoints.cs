using Contratos.Api.Security;
using Contratos.Application.Common;
using Contratos.Application.Templates;

namespace Contratos.Api.Endpoints;

internal static class TemplatesEndpoints
{
    /// <summary>CSP da pré-visualização: o HTML do template usa estilos inline; nada de script nem rede.</summary>
    public const string PreviewCsp = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; frame-ancestors 'self'";
    private const string PreviewCspWithoutAncestors = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; frame-ancestors ";

    /// <summary>
    /// Quem pode o quê. A tela usa o perfil (Admin edita); o embed usa as permissões da sessão.
    /// <paramref name="FrameAncestors"/>: quem pode emoldurar a pré-visualização.
    /// </summary>
    internal sealed record Access(
        string LocationPrefix, string EditPolicy, string PublishPolicy, string ArchivePolicy,
        Func<HttpContext, Task<string>> FrameAncestors);

    private static readonly Access App = new("/api/v1/templates", Policies.IsAdmin, Policies.IsAdmin, Policies.IsAdmin,
        _ => Task.FromResult("'self'"));

    public static RouteGroupBuilder MapTemplatesEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/templates").WithTags("Templates").RequireAuthorization().MapTemplateRoutes(App);
        return api;
    }

    /// <summary>As rotas de templates, compartilhadas pela tela (<c>/api/v1</c>) e pelo embed (<c>/api/embed/v1</c>).</summary>
    internal static RouteGroupBuilder MapTemplateRoutes(this RouteGroupBuilder g, Access access)
    {
        g.MapGet("/", async (bool? archived, ITemplateService templates, CancellationToken ct) =>
                TypedResults.Ok(await templates.ListAsync(archived ?? false, ct)))
            .WithSummary("Lista os templates (ativos ou arquivados)");

        g.MapGet("/starters", () => TypedResults.Ok(TemplateStarters.Catalog()))
            .RequireAuthorization(access.EditPolicy)
            .WithSummary("Modelos prontos para começar um template, com os setores que os agrupam");

        g.MapGet("/starters/{starterId}", (string starterId, DocumentRenderer renderer) =>
                TemplateStarters.Find(starterId) is { } s
                    ? (IResult)TypedResults.Ok(new TemplateStarterDetail(s.Id, s.Category, s.Name, s.Description, s.Variables,
                        s.DefaultSignerRoles, renderer.RenderOutline(s.BodyHtml, s.Variables)))
                    : Error.NotFound("Modelo pronto não encontrado.").ToHttp())
            .RequireAuthorization(access.EditPolicy)
            .WithSummary("Um modelo pronto: variáveis, signatários e o texto para pré-visualizar");

        g.MapPost("/preview", (PreviewTemplateRequest req, ITemplateService templates, HttpContext http) =>
                templates.PreviewUnsaved(req).ToHttp(html => Html(http, html)))
            .RequireAuthorization(access.EditPolicy)
            .WithSummary("HTML do conteúdo ainda não salvo do editor, com dados de exemplo");

        g.MapPost("/", async (CreateTemplateRequest req, ITemplateService templates, HttpContext http, CancellationToken ct) =>
                (await templates.CreateAsync(req, ct)).ToHttp(t => WithETag(http, t, TypedResults.Created($"{access.LocationPrefix}/{t.Id}", t))))
            .RequireAuthorization(access.EditPolicy)
            .WithSummary("Cria um template em branco ou a partir de um modelo pronto (starterId)");

        g.MapGet("/{id:guid}", async (Guid id, ITemplateService templates, HttpContext http, CancellationToken ct) =>
                (await templates.GetAsync(id, ct)).ToHttp(t => WithETag(http, t, TypedResults.Ok(t))))
            .WithSummary("Detalhe: versão publicada, rascunho e histórico");

        g.MapPut("/{id:guid}", async (Guid id, SaveTemplateRequest req, ITemplateService templates, HttpContext http, CancellationToken ct) =>
                (await templates.SaveDraftAsync(id, req, ETags.IfMatch(http), ct)).ToHttp(t => WithETag(http, t, TypedResults.Ok(t))))
            .RequireAuthorization(access.EditPolicy)
            .WithSummary("Salva o rascunho da próxima versão (If-Match opcional)");

        g.MapPost("/{id:guid}/publish", Admin((templates, id, ct) => templates.PublishAsync(id, ct)))
            .RequireAuthorization(access.PublishPolicy).WithSummary("Publica o rascunho como nova versão");
        g.MapDelete("/{id:guid}/draft", Admin((templates, id, ct) => templates.DiscardDraftAsync(id, ct)))
            .RequireAuthorization(access.EditPolicy).WithSummary("Descarta o rascunho");
        g.MapPost("/{id:guid}/duplicate", async (Guid id, ITemplateService templates, HttpContext http, CancellationToken ct) =>
                (await templates.DuplicateAsync(id, ct)).ToHttp(t => WithETag(http, t, TypedResults.Created($"{access.LocationPrefix}/{t.Id}", t))))
            .RequireAuthorization(access.EditPolicy).WithSummary("Duplica o template");
        g.MapPost("/{id:guid}/archive", Admin((templates, id, ct) => templates.SetArchivedAsync(id, true, ct)))
            .RequireAuthorization(access.ArchivePolicy).WithSummary("Arquiva");
        g.MapPost("/{id:guid}/unarchive", Admin((templates, id, ct) => templates.SetArchivedAsync(id, false, ct)))
            .RequireAuthorization(access.ArchivePolicy).WithSummary("Desarquiva");

        g.MapGet("/{id:guid}/versions/{versionId:guid}", async (Guid id, Guid versionId, ITemplateService templates, CancellationToken ct) =>
                (await templates.GetVersionAsync(id, versionId, ct)).ToHttp())
            .WithSummary("Conteúdo de uma versão");

        g.MapGet("/{id:guid}/preview", async (Guid id, string? source, ITemplateService templates, HttpContext http, CancellationToken ct) =>
            {
                var which = string.Equals(source, "current", StringComparison.OrdinalIgnoreCase) ? PreviewSource.Current : PreviewSource.Draft;
                var result = await templates.PreviewAsync(id, which, ct);
                if (!result.IsSuccess)
                    return result.Error!.ToHttp();
                return Html(http, result.Value!, await access.FrameAncestors(http));
            })
            .WithSummary("HTML com dados de exemplo, para o iframe de pré-visualização");

        return g;
    }

    /// <summary>Página HTML gerada (pré-visualização) com CSP própria e permissão de iframe na mesma origem.</summary>
    /// <param name="frameAncestors">No embed, a página do iframe e o site do cliente acima dela.</param>
    public static IResult Html(HttpContext http, string html, string frameAncestors = "'self'")
    {
        http.Response.Headers.ContentSecurityPolicy = PreviewCspWithoutAncestors + frameAncestors;
        // X-Frame-Options não tem como listar o site do cliente: no embed vale só o frame-ancestors.
        http.Response.Headers.XFrameOptions = frameAncestors == "'self'" ? "SAMEORIGIN" : "";
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

using Contratos.Api.Security;
using Contratos.Application.Integrations;
using Contratos.Infrastructure.Integrations;
using Contratos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Api.Endpoints;

/// <summary>
/// Embed de templates: a tela de templates dentro do sistema do cliente, num iframe.
/// <list type="bullet">
/// <item>Páginas <c>/embed/templates</c> e <c>/embed/template-editor</c>: os mesmos HTML da tela, com
/// <c>frame-ancestors</c> liberando só os domínios cadastrados pela organização.</item>
/// <item>API <c>/api/embed/v1</c>: as rotas de templates da tela, autenticadas pelo token da sessão e limitadas
/// pelas permissões dela. É a API da página, no formato da tela; o sistema do cliente usa a API pública.</item>
/// </list>
/// </summary>
internal static class EmbedEndpoints
{
    public const string ApiPrefix = "/api/embed/v1";

    public static void MapEmbedEndpoints(this WebApplication app)
    {
        app.MapGet(EmbedSessions.ListPage, (HttpContext http, EmbedSessions s, IWebHostEnvironment env, CancellationToken ct) =>
            PageAsync("templates.html", http, s, env, ct)).ExcludeFromDescription();
        app.MapGet(EmbedSessions.EditorPage, (HttpContext http, EmbedSessions s, IWebHostEnvironment env, CancellationToken ct) =>
            PageAsync("template-editor.html", http, s, env, ct)).ExcludeFromDescription();

        // Abrir o link: troca o token de uso único (do #token= da URL) pelo token de acesso.
        app.MapPost(ApiPrefix + "/session", async (EmbedOpenRequest req, EmbedSessions sessions, CancellationToken ct) =>
                await sessions.OpenAsync(req, ct) is { } opened
                    ? TypedResults.Ok(opened)
                    : ResultHttp.Problem(StatusCodes.Status401Unauthorized,
                        "Este link do embed já foi aberto ou expirou. Abra de novo pelo seu sistema.", "embed_link_invalid"))
            .AllowAnonymous()
            .ExcludeFromDescription();

        var g = app.MapGroup(ApiPrefix)
            .WithTags("Embed")
            .ExcludeFromDescription()
            .RequireAuthorization(Policies.Embed)
            .AddEndpointFilter(PublicApiEndpoints.RequirePlanAsync);

        g.MapGet("/session", async (HttpContext http, EmbedSessions sessions, CancellationToken ct) =>
            await sessions.InfoAsync(SessionId(http), ct) is { } info ? TypedResults.Ok(info) : Results.NotFound());

        g.MapDelete("/session", async (HttpContext http, EmbedSessions sessions, CancellationToken ct) =>
        {
            await sessions.EndAsync(SessionId(http), ct);
            return TypedResults.NoContent();
        });

        var templates = g.MapGroup("/templates");
        templates.MapTemplateRoutes(new TemplatesEndpoints.Access(
            ApiPrefix + "/templates", Policies.EmbedEdit, Policies.EmbedPublish, Policies.EmbedArchive,
            async http =>
            {
                var origins = await http.RequestServices.GetRequiredService<EmbedSessions>().FrameAncestorsAsync(SessionId(http), http.RequestAborted);
                return string.Join(' ', ["'self'", .. origins]);
            }));

        // O iframe da pré-visualização é carregado pelo src (sem cabeçalho): um link com ticket curto.
        templates.MapPost("/{id:guid}/preview-link", async (Guid id, string? source, HttpContext http, EmbedSessions sessions, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Templates.AnyAsync(t => t.Id == id, ct))
                return ResultHttp.Problem(StatusCodes.Status404NotFound, "Template não encontrado.", "not_found");
            var which = string.Equals(source, "current", StringComparison.OrdinalIgnoreCase) ? "current" : "draft";
            var ticket = Uri.EscapeDataString(sessions.PreviewTicket(SessionId(http), id));
            return TypedResults.Ok(new EmbedPreviewLink($"{ApiPrefix}/templates/{id}/preview?source={which}&ticket={ticket}"));
        });

        app.MapFallback(ApiPrefix + "/{**path}", () =>
            ResultHttp.Problem(StatusCodes.Status404NotFound, "Rota inexistente na API do embed.", "route_not_found"));
    }

    private static Guid SessionId(HttpContext http) =>
        Guid.Parse(http.User.FindFirst(EmbedAuthenticationHandler.SessionIdClaim)!.Value);

    /// <summary>
    /// A página da tela, com a CSP liberando o iframe nos domínios da sessão. Sessão desconhecida: ninguém emoldura
    /// (<c>'none'</c>). Sessão expirada ou já aberta: a página carrega e mostra o aviso dentro do sistema do cliente.
    /// </summary>
    private static async Task<IResult> PageAsync(string file, HttpContext http, EmbedSessions sessions, IWebHostEnvironment env, CancellationToken ct)
    {
        IReadOnlyList<string> origins = Guid.TryParse(http.Request.Query["session"], out var id)
            ? await sessions.FrameAncestorsAsync(id, ct)
            : [];
        var h = http.Response.Headers;
        h.ContentSecurityPolicy = SecurityHeaders.CspFramedBy(EmbedOrigins.FrameAncestors(origins));
        h.XFrameOptions = "";
        h["Cross-Origin-Resource-Policy"] = "cross-origin";
        h.CacheControl = "no-store";
        var page = env.WebRootFileProvider.GetFileInfo(file);
        return TypedResults.Stream(page.CreateReadStream(), "text/html; charset=utf-8");
    }
}

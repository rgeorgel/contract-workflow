namespace Contratos.Api.Security;

internal static class SecurityHeaders
{
    private const string CspBase =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; frame-src 'self'; form-action 'self'; base-uri 'self'; object-src 'none'; frame-ancestors ";
    private const string Csp = CspBase + "'none'";

    /// <summary>A CSP do site, liberando o iframe só para estes sites (páginas do embed).</summary>
    public static string CspFramedBy(string frameAncestors) => CspBase + frameAncestors;

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                var h = ctx.Response.Headers;
                // A UI do Scalar (só em dev) carrega scripts de CDN; páginas geradas (pré-visualização)
                // trazem a própria CSP; o resto do site segue a CSP restritiva.
                if (!ctx.Request.Path.StartsWithSegments("/scalar") && !h.ContainsKey("Content-Security-Policy"))
                    h.ContentSecurityPolicy = Csp;
                h.XContentTypeOptions = "nosniff";
                h["Referrer-Policy"] = "strict-origin-when-cross-origin";
                // Vazio: a rota pediu para não mandar (o frame-ancestors da CSP decide quem emoldura).
                if (!h.ContainsKey("X-Frame-Options"))
                    h.XFrameOptions = "DENY";
                else if (string.IsNullOrEmpty(h.XFrameOptions))
                    h.Remove("X-Frame-Options");
                h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                h["Cross-Origin-Opener-Policy"] = "same-origin";
                if (!h.ContainsKey("Cross-Origin-Resource-Policy"))
                    h["Cross-Origin-Resource-Policy"] = "same-origin";
                // Respostas da API têm dados da organização: nada de cache compartilhado.
                if ((ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments("/superadmin/api"))
                    && !h.ContainsKey("Cache-Control"))
                    h.CacheControl = "no-store";
                // Portal super admin: fora de buscadores.
                if (ctx.Request.Path.StartsWithSegments("/superadmin"))
                    h["X-Robots-Tag"] = "noindex, nofollow";
                return Task.CompletedTask;
            });
            await next();
        });
}

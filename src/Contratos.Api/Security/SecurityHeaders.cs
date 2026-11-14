namespace Contratos.Api.Security;

internal static class SecurityHeaders
{
    private const string Csp =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; frame-src 'self'; frame-ancestors 'none'; form-action 'self'; base-uri 'self'; object-src 'none'";

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
                if (!h.ContainsKey("X-Frame-Options"))
                    h.XFrameOptions = "DENY";
                h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                return Task.CompletedTask;
            });
            await next();
        });
}

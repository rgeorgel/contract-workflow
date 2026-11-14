using Microsoft.AspNetCore.Antiforgery;

namespace Contratos.Api.Security;

/// <summary>
/// Exige o token antiforgery (cabeçalho X-CSRF-TOKEN) em POST/PUT/PATCH/DELETE.
/// O <c>api.js</c> busca o token em <c>GET /api/v1/auth/csrf</c> e envia em toda escrita.
/// </summary>
internal sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) || HttpMethods.IsOptions(http.Request.Method))
            return await next(context);

        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Token antiforgery ausente ou inválido.",
                extensions: new Dictionary<string, object?> { ["code"] = "csrf_invalid" });
        }

        return await next(context);
    }
}

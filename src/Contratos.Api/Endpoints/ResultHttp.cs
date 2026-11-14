using Contratos.Application.Common;

namespace Contratos.Api.Endpoints;

/// <summary>Converte <see cref="Result"/> e <see cref="Error"/> em respostas HTTP (ProblemDetails).</summary>
internal static class ResultHttp
{
    public static IResult ToHttp(this Error e)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = e.Code };
        if (e.Kind == ErrorKind.Validation && e.Fields is { IsEmpty: false } fields)
            return TypedResults.ValidationProblem(fields.ToDictionary(), title: e.Title, extensions: extensions);

        var status = e.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
            _ => StatusCodes.Status400BadRequest,
        };
        return TypedResults.Problem(statusCode: status, title: e.Title, extensions: extensions);
    }

    public static IResult ToHttp(this Result r) => r.IsSuccess ? TypedResults.NoContent() : r.Error!.ToHttp();

    public static IResult ToHttp<T>(this Result<T> r, Func<T, IResult>? onSuccess = null) =>
        r.IsSuccess ? onSuccess?.Invoke(r.Value!) ?? TypedResults.Ok(r.Value) : r.Error!.ToHttp();

    public static IResult Problem(int status, string title, string code) =>
        TypedResults.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}

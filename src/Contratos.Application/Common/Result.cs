namespace Contratos.Application.Common;

public enum ErrorKind { Validation, NotFound, Conflict, Forbidden, PreconditionFailed, PaymentRequired }

public sealed record Error(ErrorKind Kind, string Title, string? Code = null, ValidationErrors? Fields = null)
{
    public static Error NotFound(string title = "Registro não encontrado.") => new(ErrorKind.NotFound, title, "not_found");
    public static Error Conflict(string title, string code) => new(ErrorKind.Conflict, title, code);
    public static Error Forbidden(string title, string code = "forbidden") => new(ErrorKind.Forbidden, title, code);
    public static Error PaymentRequired(string title, string code) => new(ErrorKind.PaymentRequired, title, code);
    public static Error Validation(ValidationErrors fields, string title = "Confira os dados informados.") =>
        new(ErrorKind.Validation, title, "validation", fields);
    public static Error Validation(string field, string message) =>
        Validation(new ValidationErrors().Add(field, message));
    public static Error Concurrency() =>
        new(ErrorKind.PreconditionFailed, "O registro foi alterado por outra pessoa. Recarregue a página.", "concurrency");
}

public readonly struct Result
{
    private Result(Error? error) => Error = error;
    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public static Result Ok() => new(null);
    public static implicit operator Result(Error error) => new(error);
}

public readonly struct Result<T>
{
    private Result(T? value, Error? error) { Value = value; Error = error; }
    public T? Value { get; }
    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    /// <summary>Para quando o valor é de um tipo interface (a conversão implícita não vale para interfaces).</summary>
    public static Result<T> Ok(T value) => new(value, null);
    public static implicit operator Result<T>(T value) => new(value, null);
    public static implicit operator Result<T>(Error error) => new(default, error);
}

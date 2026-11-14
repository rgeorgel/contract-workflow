namespace Contratos.Application.Common;

public sealed record Paged<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

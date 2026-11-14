using Contratos.Api.Security;
using Contratos.Application.Users;

namespace Contratos.Api.Endpoints;

internal static class UsersEndpoints
{
    public static RouteGroupBuilder MapUsersEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/users").WithTags("Usuários").RequireAuthorization(Policies.IsAdmin);

        g.MapGet("/", async (IUserService users, CancellationToken ct) => TypedResults.Ok(await users.ListAsync(ct)))
            .WithSummary("Usuários e convites da organização");

        g.MapPost("/invite", async (InviteUserRequest req, IUserService users, CancellationToken ct) =>
                (await users.InviteAsync(req, ct)).ToHttp(u => TypedResults.Created($"/api/v1/users/{u.Id}", u)))
            .WithSummary("Convida alguém por e-mail");

        g.MapPost("/{id:guid}/resend-invite", async (Guid id, IUserService users, CancellationToken ct) =>
                (await users.ResendInviteAsync(id, ct)).ToHttp())
            .WithSummary("Reenvia o convite");

        g.MapDelete("/{id:guid}/invite", async (Guid id, IUserService users, CancellationToken ct) =>
                (await users.CancelInviteAsync(id, ct)).ToHttp())
            .WithSummary("Cancela um convite pendente");

        g.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest req, IUserService users, CancellationToken ct) =>
                (await users.UpdateAsync(id, req, ct)).ToHttp())
            .WithSummary("Muda o perfil ou ativa/desativa");

        return api;
    }
}

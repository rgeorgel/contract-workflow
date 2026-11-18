using System.Security.Claims;
using Contratos.Application.Accounts;
using Contratos.Application.Users;
using Contratos.Infrastructure.Identity;
using Contratos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Contratos.Api.Endpoints;

public sealed record ConfirmEmailRequest(Guid UserId, string? Token);
public sealed record EmailRequest(string? Email);
public sealed record LoginRequest(string? Email, string? Password, bool RememberMe = false);
public sealed record InviteLookupRequest(Guid UserId, string? Token);
public sealed record ResetPasswordRequest(Guid UserId, string? Token, string? Password);
public sealed record CsrfResponse(string Token);
public sealed record AcceptedResponse(string Message);
/// <param name="PaymentOverdue">Assinatura com pagamento recusado: criar contratos, templates e usuários fica bloqueado.</param>
public sealed record OrganizationSummary(Guid Id, string Name, string Slug, bool PaymentOverdue);
/// <summary>Sessão de "Entrar como": quem da equipe está usando a conta e até quando.</summary>
public sealed record ImpersonationInfo(string ImpersonatorName, DateTimeOffset? ExpiresAt);
/// <param name="Impersonation">Preenchido quando alguém da equipe está usando esta conta ("Entrar como").</param>
public sealed record MeResponse(Guid Id, string Name, string Email, string Role, OrganizationSummary Organization,
    ImpersonationInfo? Impersonation = null);

internal static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    /// <summary>"Último acesso" com precisão de alguns minutos: não grava a cada página aberta.</summary>
    private static readonly TimeSpan LastSeenPrecision = TimeSpan.FromMinutes(5);

    private static IResult Suspended() => ResultHttp.Problem(StatusCodes.Status403Forbidden,
        "O acesso desta conta está suspenso. Fale com o suporte para reativar.", "account_suspended");

    private static Task<bool> IsSuspendedAsync(AppDbContext db, Guid organizationId, CancellationToken ct) =>
        db.Organizations.AnyAsync(o => o.Id == organizationId && o.SuspendedAt != null, ct);

    private static Task TouchLastSeenAsync(AppDbContext db, Guid userId, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var stale = now - LastSeenPrecision;
        return db.Users.Where(u => u.Id == userId && (u.LastSeenAt == null || u.LastSeenAt < stale))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.LastSeenAt, now), ct);
    }

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapGet("/csrf", (HttpContext ctx, IAntiforgery af) =>
                TypedResults.Ok(new CsrfResponse(af.GetAndStoreTokens(ctx).RequestToken!)))
            .WithSummary("Token antiforgery para as próximas escritas");

        auth.MapPost("/register", Register).RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Cadastro de nova organização + primeiro Admin");
        auth.MapPost("/confirm-email", ConfirmEmail).RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Confirma o e-mail com o token enviado por e-mail");
        auth.MapPost("/resend-confirmation", ResendConfirmation).RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Reenvia o e-mail de confirmação");

        auth.MapPost("/login", Login).RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Abre a sessão por cookie");
        auth.MapPost("/logout", async (SignInManager<AppUser> signIn) =>
            {
                await signIn.SignOutAsync();
                return TypedResults.NoContent();
            })
            .WithSummary("Encerra a sessão");
        auth.MapGet("/me", Me).RequireAuthorization()
            .WithSummary("Usuário logado, perfil e organização");
        auth.MapPost("/stop-impersonation", async (ClaimsPrincipal principal, Contratos.Infrastructure.Platform.ImpersonationService svc, CancellationToken ct) =>
                (await svc.StopAsync(principal, ct)).ToHttp())
            .RequireAuthorization()
            .WithSummary("Encerra a sessão de suporte (\"Entrar como\") no app");

        auth.MapPost("/forgot-password", ForgotPassword).RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Envia o link de nova senha");
        auth.MapPost("/reset-password", ResetPassword).RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Define a nova senha com o token do e-mail");

        // POST para o token não ficar em logs de URL.
        auth.MapPost("/invite/lookup", async (InviteLookupRequest req, IUserService users, CancellationToken ct) =>
                (await users.GetInviteAsync(req.UserId, req.Token ?? "", ct)).ToHttp())
            .RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Dados do convite (organização, e-mail)");
        auth.MapPost("/accept-invite", AcceptInvite).RequireRateLimiting(RateLimitPolicy)
            .WithSummary("Aceita o convite: define nome e senha e entra");

        return api;
    }

    private static async Task<IResult> Register(RegisterOrganizationRequest request, IAccountService accounts, CancellationToken ct)
    {
        var errors = await accounts.RegisterAsync(request, ct);
        if (!errors.IsEmpty)
            return TypedResults.ValidationProblem(errors.ToDictionary(), title: "Confira os dados do cadastro.");

        return TypedResults.Accepted((string?)null, new AcceptedResponse("Enviamos um link de confirmação para o seu e-mail."));
    }

    private static async Task<IResult> ConfirmEmail(ConfirmEmailRequest request, IAccountService accounts, CancellationToken ct)
    {
        if (request.UserId == Guid.Empty || string.IsNullOrWhiteSpace(request.Token)
            || !await accounts.ConfirmEmailAsync(request.UserId, request.Token, ct))
        {
            return ResultHttp.Problem(StatusCodes.Status400BadRequest, "Link de confirmação inválido ou expirado.", "invalid_token");
        }

        return TypedResults.NoContent();
    }

    private static async Task<Accepted<AcceptedResponse>> ResendConfirmation(EmailRequest request, IAccountService accounts, CancellationToken ct)
    {
        await accounts.ResendConfirmationAsync(request.Email ?? "", ct);
        return TypedResults.Accepted((string?)null,
            new AcceptedResponse("Se houver um cadastro pendente para este e-mail, enviamos um novo link."));
    }

    private static async Task<IResult> Login(LoginRequest request, SignInManager<AppUser> signIn, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var invalid = ResultHttp.Problem(StatusCodes.Status401Unauthorized, "E-mail ou senha incorretos.", "invalid_credentials");
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            return invalid;

        var user = await signIn.UserManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            return invalid;

        // Confere a senha antes de abrir a sessão, para barrar usuário desativado.
        var check = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (check.IsLockedOut)
            return ResultHttp.Problem(StatusCodes.Status423Locked, "Muitas tentativas. Tente de novo em alguns minutos ou redefina a senha.", "locked_out");
        if (check.IsNotAllowed)
            return ResultHttp.Problem(StatusCodes.Status403Forbidden, "Confirme seu e-mail antes de entrar.", "email_not_confirmed");
        if (!check.Succeeded)
            return invalid;
        if (user.DeactivatedAt is not null)
            return ResultHttp.Problem(StatusCodes.Status403Forbidden, "Seu acesso foi desativado. Fale com um Admin da sua empresa.", "account_disabled");
        if (await IsSuspendedAsync(db, user.OrganizationId, ct))
            return Suspended();

        await signIn.SignInAsync(user, request.RememberMe);
        await TouchLastSeenAsync(db, user.Id, clock, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> Me(
        ClaimsPrincipal principal, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return TypedResults.Unauthorized();

        // Conta suspensa: a tela volta ao login, que explica o motivo (o cookie cai em até 1 minuto).
        var me = await db.Users
            .Where(u => u.Id == userId && u.DeactivatedAt == null)
            .Join(db.Organizations.Where(o => o.SuspendedAt == null), u => u.OrganizationId, o => o.Id, (u, o) => new MeResponse(
                u.Id, u.Name, u.Email!, u.Role.ToString(), new OrganizationSummary(o.Id, o.Name, o.Slug, o.SubscriptionStatus == "past_due")))
            .SingleOrDefaultAsync(ct);
        if (me is null)
            return TypedResults.Unauthorized();

        // "Entrar como" não conta como acesso do cliente.
        if (principal.FindFirstValue(AppClaims.ImpersonatorName) is { } impersonator)
            return TypedResults.Ok(me with
            {
                Impersonation = new ImpersonationInfo(impersonator,
                    DateTimeOffset.TryParse(principal.FindFirstValue(AppClaims.ImpersonationExpires), System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var expires) ? expires : null),
            });

        await TouchLastSeenAsync(db, userId, clock, ct);
        return TypedResults.Ok(me);
    }

    private static async Task<Accepted<AcceptedResponse>> ForgotPassword(EmailRequest request, IAccountService accounts, CancellationToken ct)
    {
        await accounts.ForgotPasswordAsync(request.Email ?? "", ct);
        return TypedResults.Accepted((string?)null,
            new AcceptedResponse("Se houver uma conta com este e-mail, enviamos um link para criar uma nova senha."));
    }

    private static async Task<IResult> ResetPassword(ResetPasswordRequest request, IAccountService accounts, CancellationToken ct) =>
        (await accounts.ResetPasswordAsync(request.UserId, request.Token ?? "", request.Password ?? "", ct)).ToHttp();

    private static async Task<IResult> AcceptInvite(
        AcceptInviteRequest request, IUserService users, SignInManager<AppUser> signIn, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var result = await users.AcceptInviteAsync(request, ct);
        if (!result.IsSuccess)
            return result.Error!.ToHttp();

        var user = await signIn.UserManager.FindByIdAsync(result.Value.ToString());
        // A senha fica definida; entrar, só depois de a conta ser reativada.
        if (await IsSuspendedAsync(db, user!.OrganizationId, ct))
            return Suspended();
        await signIn.SignInAsync(user, isPersistent: false);
        await TouchLastSeenAsync(db, user.Id, clock, ct);
        return TypedResults.NoContent();
    }
}

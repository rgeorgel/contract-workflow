using Contratos.Application.Common;

namespace Contratos.Application.Accounts;

public interface IAccountService
{
    /// <summary>
    /// Cria a organização e o primeiro Admin e envia o e-mail de confirmação.
    /// Não revela se o e-mail já tem conta: nesse caso envia um aviso ao dono do e-mail.
    /// </summary>
    Task<ValidationErrors> RegisterAsync(RegisterOrganizationRequest request, CancellationToken ct);

    Task<bool> ConfirmEmailAsync(Guid userId, string token, CancellationToken ct);

    /// <summary>Reenvia a confirmação se houver conta não confirmada; nunca revela se o e-mail existe.</summary>
    Task ResendConfirmationAsync(string email, CancellationToken ct);

    /// <summary>Envia o link de nova senha se a conta existir e estiver ativa; nunca revela se o e-mail existe.</summary>
    Task ForgotPasswordAsync(string email, CancellationToken ct);

    Task<Result> ResetPasswordAsync(Guid userId, string token, string password, CancellationToken ct);
}

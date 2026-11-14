using System.Net.Mail;
using Contratos.Application.Common;
using Contratos.Domain.Organizations;

namespace Contratos.Application.Accounts;

public sealed record RegisterOrganizationRequest(
    string? OrganizationName,
    string? Name,
    string? Email,
    string? Password);

public static class RegisterOrganizationValidator
{
    public const int PasswordMinLength = 6;
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 256;

    public static ValidationErrors Validate(RegisterOrganizationRequest req)
    {
        var errors = new ValidationErrors();

        var org = req.OrganizationName?.Trim() ?? "";
        if (org.Length < 2)
            errors.Add("organizationName", "Informe o nome da empresa.");
        else if (org.Length > Organization.NameMaxLength)
            errors.Add("organizationName", $"Use no máximo {Organization.NameMaxLength} caracteres.");

        var name = req.Name?.Trim() ?? "";
        if (name.Length < 2)
            errors.Add("name", "Informe seu nome.");
        else if (name.Length > NameMaxLength)
            errors.Add("name", $"Use no máximo {NameMaxLength} caracteres.");

        var email = req.Email?.Trim() ?? "";
        if (email.Length == 0)
            errors.Add("email", "Informe seu e-mail.");
        else if (email.Length > EmailMaxLength || !IsValidEmail(email))
            errors.Add("email", "E-mail inválido.");

        var password = req.Password ?? "";
        if (password.Length < PasswordMinLength)
            errors.Add("password", $"A senha precisa ter ao menos {PasswordMinLength} caracteres.");

        return errors;
    }

    public static bool IsValidEmail(string email)
    {
        if (email.Contains(' ') || !email.Contains('@'))
            return false;
        try
        {
            var addr = new MailAddress(email);
            return addr.Address == email && addr.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

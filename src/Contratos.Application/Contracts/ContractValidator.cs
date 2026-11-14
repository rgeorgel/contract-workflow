using System.Text.RegularExpressions;
using Contratos.Application.Accounts;
using Contratos.Application.Common;
using Contratos.Domain.Contracts;
using Contratos.Domain.Templates;

namespace Contratos.Application.Contracts;

public static partial class ContractValidator
{
    /// <summary>Telefone em formato internacional, ex.: +5511987654321.</summary>
    [GeneratedRegex(@"^\+[1-9]\d{9,14}$")]
    public static partial Regex PhonePattern();

    public static ValidationErrors ValidateDraft(UpdateContractRequest req, IReadOnlyList<TemplateVariable> variables, DateOnly today)
    {
        var errors = new ValidationErrors();
        var title = req.Title?.Trim() ?? "";
        if (title.Length < 2)
            errors.Add("title", "Informe o título do contrato.");
        else if (title.Length > Contract.TitleMaxLength)
            errors.Add("title", $"Use no máximo {Contract.TitleMaxLength} caracteres.");
        if ((req.Message?.Length ?? 0) > Contract.MessageMaxLength)
            errors.Add("message", $"Use no máximo {Contract.MessageMaxLength} caracteres.");
        if (req.ExpiresAt is { } exp && exp <= today)
            errors.Add("expiresAt", "A data de expiração precisa ser no futuro.");
        if (req.SigningMode is { } mode && !Enum.IsDefined(mode))
            errors.Add("signingMode", "Modo de assinatura inválido.");

        var values = req.VariableValues ?? [];
        foreach (var v in variables)
        {
            if (values.TryGetValue(v.Key, out var value) && VariableValues.Validate(v.Type, value) is { } message)
                errors.Add($"variableValues.{v.Key}", message);
        }
        return errors;
    }

    /// <summary>Regras de envio: obrigatórias preenchidas e ao menos um signatário.</summary>
    public static ValidationErrors ValidateSend(Contract contract, IReadOnlyList<TemplateVariable> variables, DateTimeOffset now)
    {
        var errors = new ValidationErrors();
        foreach (var v in variables)
        {
            contract.VariableValues.TryGetValue(v.Key, out var value);
            if (v.Required && string.IsNullOrWhiteSpace(value))
                errors.Add($"variableValues.{v.Key}", $"Preencha “{v.Label}”.");
            else if (VariableValues.Validate(v.Type, value) is { } message)
                errors.Add($"variableValues.{v.Key}", $"{v.Label}: {message}");
        }
        if (contract.Signers.Count == 0)
            errors.Add("signers", "Adicione ao menos um signatário.");
        if (contract.ExpiresAt is { } exp && exp <= now)
            errors.Add("expiresAt", "A data de expiração já passou.");
        return errors;
    }

    public static ValidationErrors ValidateSigner(SignerRequest req, IEnumerable<ContractSigner> others)
    {
        var errors = new ValidationErrors();
        var name = req.Name?.Trim() ?? "";
        if (name.Length < 2)
            errors.Add("name", "Informe o nome.");
        else if (name.Length > 100)
            errors.Add("name", "Use no máximo 100 caracteres.");

        var method = req.DeliveryMethod ?? DeliveryMethod.Email;
        if (!Enum.IsDefined(method))
            errors.Add("deliveryMethod", "Canal inválido.");

        var email = req.Email?.Trim() ?? "";
        if (email.Length > 0 && !RegisterOrganizationValidator.IsValidEmail(email))
            errors.Add("email", "E-mail inválido.");
        else if (method == DeliveryMethod.Email && email.Length == 0)
            errors.Add("email", "Informe o e-mail: é por ele que o link chega.");
        else if (email.Length > 0 && others.Any(o => string.Equals(o.Email, email, StringComparison.OrdinalIgnoreCase)))
            errors.Add("email", "Já há um signatário com este e-mail.");

        var phone = NormalizePhone(req.Phone);
        if (!string.IsNullOrWhiteSpace(req.Phone) && req.Phone.Any(ch => !(char.IsAsciiDigit(ch) || " +-().".Contains(ch))))
            errors.Add("phone", "Use só números, ex.: +55 11 98765-4321.");
        else if (phone.Length > 0 && !PhonePattern().IsMatch(phone))
            errors.Add("phone", "Use o formato internacional, ex.: +55 11 98765-4321.");
        else if (method is DeliveryMethod.WhatsApp or DeliveryMethod.Sms && phone.Length == 0)
            errors.Add("phone", "Informe o telefone: é por ele que o link chega.");

        if (!string.IsNullOrWhiteSpace(req.Cpf) && !Documents.IsValidCpf(req.Cpf))
            errors.Add("cpf", "CPF inválido.");
        if ((req.Role?.Length ?? 0) > 60)
            errors.Add("role", "Use no máximo 60 caracteres.");
        if (req.Action is { } action && !Enum.IsDefined(action))
            errors.Add("action", "Ação inválida.");
        if (req.SignOrder is < 1 or > 100)
            errors.Add("signOrder", "A ordem vai de 1 a 100.");
        return errors;
    }

    /// <summary>
    /// "+55 (11) 98765-4321" → "+5511987654321". Sem "+", um número de 10 ou 11 dígitos é tratado como
    /// brasileiro (DDD + número) e ganha o +55; 12 ou 13 dígitos começando com 55 já trazem o país.
    /// </summary>
    public static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return "";
        var digits = Documents.Digits(phone);
        if (digits.Length == 0)
            return "";
        if (phone.TrimStart().StartsWith('+'))
            return "+" + digits;
        return digits.Length is 10 or 11 ? "+55" + digits : "+" + digits;
    }
}

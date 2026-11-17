using Contratos.Application.Common;
using Contratos.Domain.Templates;

namespace Contratos.Application.Templates;

public static class TemplateValidator
{
    public const int BodyMaxLength = 200_000;
    public const int MaxVariables = 100;
    public const int MaxRoles = 20;

    /// <summary>Regras para salvar (rascunho pode estar incompleto, mas não malformado).</summary>
    public static ValidationErrors ValidateSave(SaveTemplateRequest req)
    {
        var errors = new ValidationErrors();
        var name = req.Name?.Trim() ?? "";
        if (name.Length < 2)
            errors.Add("name", "Informe o nome do template.");
        else if (name.Length > Template.NameMaxLength)
            errors.Add("name", $"Use no máximo {Template.NameMaxLength} caracteres.");
        if ((req.Description?.Length ?? 0) > Template.DescriptionMaxLength)
            errors.Add("description", $"Use no máximo {Template.DescriptionMaxLength} caracteres.");
        if ((req.BodyHtml?.Length ?? 0) > BodyMaxLength)
            errors.Add("bodyHtml", "O texto do template está grande demais.");

        var variables = req.Variables ?? [];
        if (variables.Count > MaxVariables)
            errors.Add("variables", $"Use no máximo {MaxVariables} variáveis.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < variables.Count; i++)
        {
            var v = variables[i];
            if (string.IsNullOrWhiteSpace(v.Key) || !Placeholders.KeyPattern().IsMatch(v.Key))
                errors.Add($"variables[{i}].key", "Use letras, números, _ e pontos, começando por letra (ex.: contratante.nome).");
            else if (!seen.Add(v.Key))
                errors.Add($"variables[{i}].key", $"A variável {v.Key} aparece duas vezes.");
            if (string.IsNullOrWhiteSpace(v.Label) || v.Label.Length > 100)
                errors.Add($"variables[{i}].label", "Informe um rótulo de até 100 caracteres.");
            if (!Enum.IsDefined(v.Type))
                errors.Add($"variables[{i}].type", "Tipo inválido.");
        }

        var roles = req.DefaultSignerRoles ?? [];
        if (roles.Count > MaxRoles)
            errors.Add("defaultSignerRoles", $"Use no máximo {MaxRoles} papéis.");
        for (var i = 0; i < roles.Count; i++)
        {
            var r = roles[i];
            if (string.IsNullOrWhiteSpace(r.Role) || r.Role.Length > 60)
                errors.Add($"defaultSignerRoles[{i}].role", "Informe o papel (até 60 caracteres).");
            if (!Enum.IsDefined(r.Action))
                errors.Add($"defaultSignerRoles[{i}].action", "Ação inválida.");
            if (r.Order is < 1 or > 100)
                errors.Add($"defaultSignerRoles[{i}].order", "A ordem vai de 1 a 100.");
        }

        return errors;
    }

    /// <summary>Regras extras para publicar: corpo preenchido e toda variável usada declarada.</summary>
    public static ValidationErrors ValidatePublish(string bodyHtml, IReadOnlyList<TemplateVariable> variables)
    {
        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(bodyHtml))
        {
            errors.Add("bodyHtml", "Escreva o texto do contrato antes de publicar.");
            return errors;
        }

        if (Placeholders.CheckConditionals(bodyHtml) is { } unbalanced)
            errors.Add("bodyHtml", unbalanced);

        var declared = variables.Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
        var missing = Placeholders.Extract(bodyHtml).Where(k => !declared.Contains(k)).ToList();
        if (missing.Count > 0)
            errors.Add("bodyHtml", $"Cadastre as variáveis usadas no texto: {string.Join(", ", missing)}.");

        return errors;
    }
}

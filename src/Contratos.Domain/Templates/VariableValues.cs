using System.Globalization;

namespace Contratos.Domain.Templates;

/// <summary>Validação e formatação dos valores das variáveis, por tipo.</summary>
public static class VariableValues
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Mensagem de erro, ou null se o valor é válido para o tipo. Vazio é válido aqui (obrigatoriedade é à parte).</summary>
    public static string? Validate(VariableType type, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return type switch
        {
            VariableType.Text => value.Length > 2000 ? "Use no máximo 2000 caracteres." : null,
            VariableType.Number => TryNumber(value, out _) ? null : "Informe um número.",
            VariableType.Money => TryNumber(value, out _) ? null : "Informe um valor, ex.: 1500,00.",
            VariableType.Date => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                ? null : "Informe uma data válida.",
            VariableType.CpfCnpj => Documents.IsValidCpfOrCnpj(value) ? null : "CPF ou CNPJ inválido.",
            _ => null,
        };
    }

    /// <summary>Valor como aparece no documento (datas, moeda e CPF/CNPJ no formato brasileiro).</summary>
    public static string Format(VariableType type, string value) => type switch
    {
        VariableType.Number when TryNumber(value, out var n) => n.ToString("#,##0.##", PtBr),
        VariableType.Money when TryNumber(value, out var m) => "R$\u00a0" + m.ToString("#,##0.00", PtBr),
        VariableType.Date when DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        VariableType.CpfCnpj => Documents.Format(value),
        _ => value,
    };

    public static string Sample(TemplateVariable v) => v.Type switch
    {
        VariableType.Number => "10",
        VariableType.Money => "1500.00",
        VariableType.Date => DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        VariableType.CpfCnpj => "52998224725",
        _ => v.Label,
    };

    /// <summary>Aceita "1500.5", "1.500,50" e "1500,50".</summary>
    public static bool TryNumber(string value, out decimal result)
    {
        var s = value.Trim().Replace("R$", "", StringComparison.Ordinal).Trim();
        if (s.Contains(',', StringComparison.Ordinal))
            s = s.Replace(".", "", StringComparison.Ordinal).Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }
}

public static class Documents
{
    public static string Digits(string value) => new(value.Where(char.IsAsciiDigit).ToArray());

    public static bool IsValidCpfOrCnpj(string value)
    {
        var d = Digits(value);
        return d.Length switch { 11 => IsValidCpf(d), 14 => IsValidCnpj(d), _ => false };
    }

    public static bool IsValidCpf(string value)
    {
        var d = Digits(value);
        if (d.Length != 11 || d.Distinct().Count() == 1)
            return false;
        return CheckDigit(d, 9, 10) == d[9] - '0' && CheckDigit(d, 10, 11) == d[10] - '0';

        static int CheckDigit(string d, int count, int weight)
        {
            var sum = 0;
            for (var i = 0; i < count; i++)
                sum += (d[i] - '0') * (weight - i);
            var r = sum % 11;
            return r < 2 ? 0 : 11 - r;
        }
    }

    public static bool IsValidCnpj(string value)
    {
        var d = Digits(value);
        if (d.Length != 14 || d.Distinct().Count() == 1)
            return false;
        int[] w1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] w2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        return Check(d, w1) == d[12] - '0' && Check(d, w2) == d[13] - '0';

        static int Check(string d, int[] weights)
        {
            var sum = weights.Select((w, i) => (d[i] - '0') * w).Sum();
            var r = sum % 11;
            return r < 2 ? 0 : 11 - r;
        }
    }

    public static string Format(string value)
    {
        var d = Digits(value);
        return d.Length switch
        {
            11 => $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}",
            14 => $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}",
            _ => value,
        };
    }
}

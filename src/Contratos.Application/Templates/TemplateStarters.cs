using Contratos.Domain.Templates;

namespace Contratos.Application.Templates;

/// <summary>Modelo pronto para começar um template sem a página em branco.</summary>
/// <param name="Category">Id de uma das <see cref="TemplateStarters.Categories"/>.</param>
public sealed record TemplateStarter(
    string Id, string Category, string Name, string Description, string BodyHtml,
    IReadOnlyList<TemplateVariable> Variables, IReadOnlyList<SignerRoleSuggestion> DefaultSignerRoles);

/// <summary>Setor que agrupa os modelos na galeria do "Novo template".</summary>
public sealed record TemplateStarterCategory(string Id, string Name, string Description);

/// <summary>Cartão da galeria: o suficiente para escolher, sem o texto.</summary>
/// <param name="Signers">Papéis sugeridos, na ordem de assinatura (ex.: Locador, Locatário).</param>
public sealed record TemplateStarterItem(
    string Id, string Category, string Name, string Description, int VariableCount, IReadOnlyList<string> Signers);

public sealed record TemplateStarterCatalog(
    IReadOnlyList<TemplateStarterCategory> Categories, IReadOnlyList<TemplateStarterItem> Starters);

/// <summary>Um modelo aberto na galeria, com o texto já montado para a pré-visualização.</summary>
/// <param name="PreviewHtml">Corpo sanitizado, com cada variável trocada pelo rótulo em destaque e os trechos opcionais visíveis.</param>
public sealed record TemplateStarterDetail(
    string Id, string Category, string Name, string Description,
    IReadOnlyList<TemplateVariable> Variables, IReadOnlyList<SignerRoleSuggestion> DefaultSignerRoles, string PreviewHtml);

/// <summary>
/// Os modelos de partida oferecidos ao criar um template, agrupados por setor. São textos de referência:
/// o cliente revisa e adapta antes de publicar. Cada setor fica num arquivo em <c>Templates/Starters</c>.
/// </summary>
public static partial class TemplateStarters
{
    public static IReadOnlyList<TemplateStarterCategory> Categories { get; } =
    [
        new("geral", "Geral", "Contratos e termos que servem a qualquer negócio."),
        new("advocacia", "Advocacia", "Honorários, procurações e documentos do escritório."),
        new("imobiliario", "Imobiliário", "Locação, administração e venda de imóveis."),
        new("contabilidade", "Contabilidade", "Serviços contábeis, BPO e documentos com o cliente."),
        new("rh", "Recursos humanos", "Admissão, estágio, teletrabalho e termos com colaboradores."),
        new("comercial", "Comercial", "Vendas, fornecimento e representação."),
        new("saude", "Saúde", "Consentimento, tratamento e uso de imagem em clínicas e consultórios."),
        new("educacao", "Educação", "Matrícula, cursos e autorizações de escolas e professores."),
    ];

    public static IReadOnlyList<TemplateStarter> All { get; } =
    [
        .. Geral(),
        .. Advocacia(),
        .. Imobiliario(),
        .. Contabilidade(),
        .. RecursosHumanos(),
        .. Comercial(),
        .. Saude(),
        .. Educacao(),
    ];

    public static TemplateStarter? Find(string? id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id?.Trim(), StringComparison.Ordinal));

    public static TemplateStarterCatalog Catalog() => new(Categories, All
        .Select(s => new TemplateStarterItem(s.Id, s.Category, s.Name, s.Description, s.Variables.Count,
            [.. s.DefaultSignerRoles.OrderBy(r => r.Order).Select(r => r.Role)]))
        .ToList());

    private static TemplateVariable Text(string key, string label, bool required = true) => new(key, label, VariableType.Text, required);
    private static TemplateVariable Doc(string key, string label, bool required = true) => new(key, label, VariableType.CpfCnpj, required);
    private static TemplateVariable Money(string key, string label, bool required = true) => new(key, label, VariableType.Money, required);
    private static TemplateVariable Date(string key, string label, bool required = true) => new(key, label, VariableType.Date, required);
    private static TemplateVariable Number(string key, string label, bool required = true) => new(key, label, VariableType.Number, required);

    private static SignerRoleSuggestion Signs(string role, int order) => new(role, SignerAction.Sign, order);
    private static SignerRoleSuggestion Witness(int order) => new("Testemunha", SignerAction.SignAsWitness, order);

    /// <summary>Cidade e data da assinatura, no fecho de quase todo modelo.</summary>
    private static TemplateVariable[] Closing() =>
    [
        Text("assinatura.cidade", "Cidade da assinatura"),
        Date("assinatura.data", "Data da assinatura"),
    ];

    /// <summary>Foro, cidade e data da assinatura.</summary>
    private static TemplateVariable[] Forum() => [Text("foro.cidade", "Cidade do foro"), .. Closing()];
}

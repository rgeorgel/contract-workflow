using Contratos.Domain.Templates;

namespace Contratos.Application.Templates;

/// <summary>Modelo pronto para começar um template sem a página em branco.</summary>
public sealed record TemplateStarter(
    string Id, string Name, string Description, string BodyHtml,
    IReadOnlyList<TemplateVariable> Variables, IReadOnlyList<SignerRoleSuggestion> DefaultSignerRoles);

public sealed record TemplateStarterItem(string Id, string Name, string Description, int VariableCount);

/// <summary>
/// Os modelos de partida oferecidos ao criar um template. São textos de referência:
/// o cliente revisa e adapta antes de publicar.
/// </summary>
public static class TemplateStarters
{
    public static IReadOnlyList<TemplateStarter> All { get; } =
    [
        PrestacaoDeServicos(),
        Confidencialidade(),
        LocacaoResidencial(),
    ];

    public static TemplateStarter? Find(string? id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id?.Trim(), StringComparison.Ordinal));

    private static TemplateVariable Text(string key, string label, bool required = true) => new(key, label, VariableType.Text, required);
    private static TemplateVariable Doc(string key, string label) => new(key, label, VariableType.CpfCnpj, true);
    private static TemplateVariable Money(string key, string label) => new(key, label, VariableType.Money, true);
    private static TemplateVariable Date(string key, string label) => new(key, label, VariableType.Date, true);
    private static TemplateVariable Number(string key, string label) => new(key, label, VariableType.Number, true);

    private static TemplateStarter PrestacaoDeServicos() => new(
        "prestacao-servicos",
        "Prestação de serviços",
        "Contratação de um serviço com escopo, valor, prazo e forma de pagamento.",
        """
        <h1>Contrato de Prestação de Serviços</h1>
        <p><strong>CONTRATANTE:</strong> {{contratante.nome}}, inscrito(a) no CPF/CNPJ sob o nº {{contratante.documento}}, com endereço em {{contratante.endereco}}.</p>
        <p><strong>CONTRATADO(A):</strong> {{contratado.nome}}, inscrito(a) no CPF/CNPJ sob o nº {{contratado.documento}}, com endereço em {{contratado.endereco}}.</p>
        <p>As partes acima identificadas têm entre si justo e acertado o presente Contrato de Prestação de Serviços, que se regerá pelas cláusulas a seguir.</p>

        <h2>Cláusula 1ª – Do objeto</h2>
        <p>O presente contrato tem por objeto a prestação, pelo(a) CONTRATADO(A), dos seguintes serviços: {{servico.descricao}}.</p>

        <h2>Cláusula 2ª – Do prazo</h2>
        <p>Os serviços serão prestados de {{prazo.inicio}} a {{prazo.fim}}, podendo o prazo ser prorrogado por acordo escrito entre as partes.</p>

        <h2>Cláusula 3ª – Do preço e do pagamento</h2>
        <p>Pelos serviços, o CONTRATANTE pagará ao(à) CONTRATADO(A) o valor total de {{valor.total}}, nas seguintes condições: {{pagamento.condicoes}}.</p>
        <p>O atraso no pagamento sujeitará o CONTRATANTE a multa de 2% (dois por cento) sobre o valor devido, acrescida de juros de 1% (um por cento) ao mês.</p>

        <h2>Cláusula 4ª – Das obrigações do(a) CONTRATADO(A)</h2>
        <p>O(A) CONTRATADO(A) se obriga a executar os serviços com zelo e qualidade, dentro do prazo combinado, e a manter o CONTRATANTE informado sobre o andamento dos trabalhos.</p>

        <h2>Cláusula 5ª – Das obrigações do CONTRATANTE</h2>
        <p>O CONTRATANTE se obriga a fornecer as informações e os materiais necessários à execução dos serviços e a efetuar os pagamentos nas datas acordadas.</p>

        <h2>Cláusula 6ª – Da ausência de vínculo</h2>
        <p>Este contrato não gera vínculo empregatício entre as partes, cabendo a cada uma o cumprimento das próprias obrigações trabalhistas, previdenciárias e fiscais.</p>

        <h2>Cláusula 7ª – Da rescisão</h2>
        <p>Qualquer das partes poderá rescindir este contrato mediante aviso por escrito com 30 (trinta) dias de antecedência, sendo devidos os valores dos serviços já prestados até a data da rescisão.</p>

        <h2>Cláusula 8ª – Do foro</h2>
        <p>Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer dúvidas oriundas deste contrato.</p>

        <p>E, por estarem assim justas e contratadas, as partes assinam o presente instrumento eletronicamente.</p>
        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("contratante.nome", "Nome do contratante"),
            Doc("contratante.documento", "CPF/CNPJ do contratante"),
            Text("contratante.endereco", "Endereço do contratante"),
            Text("contratado.nome", "Nome do contratado"),
            Doc("contratado.documento", "CPF/CNPJ do contratado"),
            Text("contratado.endereco", "Endereço do contratado"),
            Text("servico.descricao", "Descrição dos serviços"),
            Date("prazo.inicio", "Início dos serviços"),
            Date("prazo.fim", "Fim dos serviços"),
            Money("valor.total", "Valor total"),
            Text("pagamento.condicoes", "Condições de pagamento"),
            Text("foro.cidade", "Cidade do foro"),
            Text("assinatura.cidade", "Cidade da assinatura"),
            Date("assinatura.data", "Data da assinatura"),
        ],
        [
            new("Contratante", SignerAction.Sign, 1),
            new("Contratado", SignerAction.Sign, 2),
        ]);

    private static TemplateStarter Confidencialidade() => new(
        "confidencialidade",
        "Acordo de confidencialidade (NDA)",
        "Sigilo mútuo sobre informações trocadas entre duas partes, com prazo e multa.",
        """
        <h1>Acordo de Confidencialidade</h1>
        <p><strong>PRIMEIRA PARTE:</strong> {{parte1.nome}}, inscrita no CPF/CNPJ sob o nº {{parte1.documento}}, com endereço em {{parte1.endereco}}.</p>
        <p><strong>SEGUNDA PARTE:</strong> {{parte2.nome}}, inscrita no CPF/CNPJ sob o nº {{parte2.documento}}, com endereço em {{parte2.endereco}}.</p>
        <p>As partes, doravante denominadas em conjunto “Partes”, celebram o presente Acordo de Confidencialidade, nos termos a seguir.</p>

        <h2>Cláusula 1ª – Da finalidade</h2>
        <p>As Partes pretendem trocar informações com a seguinte finalidade: {{finalidade}} (“Finalidade”). As informações recebidas só poderão ser usadas para essa Finalidade.</p>

        <h2>Cláusula 2ª – Das informações confidenciais</h2>
        <p>Consideram-se confidenciais todas as informações técnicas, comerciais, financeiras, estratégicas, dados pessoais e quaisquer outras informações reveladas por uma Parte à outra, por qualquer meio, que não sejam de conhecimento público.</p>
        <p>Não são confidenciais as informações que: (a) já eram de conhecimento público ou passaram a ser sem culpa da Parte receptora; (b) já estavam legitimamente em poder da Parte receptora antes da revelação; ou (c) devam ser reveladas por ordem judicial ou exigência legal, hipótese em que a Parte receptora avisará a outra antes de revelá-las, sempre que permitido.</p>

        <h2>Cláusula 3ª – Das obrigações</h2>
        <p>A Parte receptora se obriga a manter sigilo sobre as informações confidenciais, a restringir o acesso a elas apenas às pessoas que precisem conhecê-las para a Finalidade e que estejam sujeitas a dever de sigilo equivalente, e a devolvê-las ou destruí-las quando solicitado.</p>

        <h2>Cláusula 4ª – Da vigência</h2>
        <p>As obrigações deste acordo vigoram durante o relacionamento entre as Partes e por {{vigencia.anos}} anos após o seu encerramento.</p>

        <h2>Cláusula 5ª – Da penalidade</h2>
        <p>O descumprimento deste acordo sujeitará a Parte infratora ao pagamento de multa de {{multa.valor}}, sem prejuízo da indenização por perdas e danos que excederem esse valor.</p>

        <h2>Cláusula 6ª – Do foro</h2>
        <p>Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer dúvidas oriundas deste acordo.</p>

        <p>E, por estarem de acordo, as Partes assinam o presente instrumento eletronicamente.</p>
        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("parte1.nome", "Nome da primeira parte"),
            Doc("parte1.documento", "CPF/CNPJ da primeira parte"),
            Text("parte1.endereco", "Endereço da primeira parte"),
            Text("parte2.nome", "Nome da segunda parte"),
            Doc("parte2.documento", "CPF/CNPJ da segunda parte"),
            Text("parte2.endereco", "Endereço da segunda parte"),
            Text("finalidade", "Finalidade da troca de informações"),
            Number("vigencia.anos", "Anos de sigilo após o encerramento"),
            Money("multa.valor", "Valor da multa"),
            Text("foro.cidade", "Cidade do foro"),
            Text("assinatura.cidade", "Cidade da assinatura"),
            Date("assinatura.data", "Data da assinatura"),
        ],
        [
            new("Primeira parte", SignerAction.Sign, 1),
            new("Segunda parte", SignerAction.Sign, 2),
        ]);

    private static TemplateStarter LocacaoResidencial() => new(
        "locacao-residencial",
        "Locação residencial",
        "Aluguel de imóvel para moradia, com valor, vencimento, prazo, reajuste e garantia.",
        """
        <h1>Contrato de Locação Residencial</h1>
        <p><strong>LOCADOR(A):</strong> {{locador.nome}}, inscrito(a) no CPF/CNPJ sob o nº {{locador.documento}}, com endereço em {{locador.endereco}}.</p>
        <p><strong>LOCATÁRIO(A):</strong> {{locatario.nome}}, inscrito(a) no CPF/CNPJ sob o nº {{locatario.documento}}, com endereço em {{locatario.endereco}}.</p>
        <p>As partes celebram o presente Contrato de Locação Residencial, regido pela Lei nº 8.245/1991 e pelas cláusulas a seguir.</p>

        <h2>Cláusula 1ª – Do imóvel</h2>
        <p>O objeto deste contrato é a locação do imóvel situado em {{imovel.endereco}}, destinado exclusivamente à moradia do(a) LOCATÁRIO(A) e de sua família.</p>

        <h2>Cláusula 2ª – Do prazo</h2>
        <p>A locação terá prazo de {{prazo.meses}} meses, com início em {{prazo.inicio}}. Findo o prazo, a locação poderá ser renovada ou prorrogada nos termos da lei.</p>

        <h2>Cláusula 3ª – Do aluguel</h2>
        <p>O aluguel mensal é de {{aluguel.valor}}, a ser pago até o dia {{aluguel.vencimento}} de cada mês. O atraso sujeitará o(a) LOCATÁRIO(A) a multa de 10% (dez por cento) sobre o valor devido, acrescida de juros de 1% (um por cento) ao mês.</p>
        <p>O aluguel será reajustado a cada 12 (doze) meses pela variação do índice {{reajuste.indice}}.</p>

        <h2>Cláusula 4ª – Dos encargos</h2>
        <p>Além do aluguel, cabe ao(à) LOCATÁRIO(A) o pagamento das contas de consumo (água, energia, gás), das despesas ordinárias de condomínio e do IPTU, salvo acordo escrito em contrário.</p>

        <h2>Cláusula 5ª – Da garantia</h2>
        <p>Em garantia das obrigações deste contrato, fica estabelecida a seguinte modalidade: {{garantia.descricao}}.</p>

        <h2>Cláusula 6ª – Da conservação e das benfeitorias</h2>
        <p>O(A) LOCATÁRIO(A) declara receber o imóvel em bom estado de conservação e se obriga a devolvê-lo no mesmo estado, ressalvado o desgaste natural pelo uso. Benfeitorias dependem de autorização prévia e por escrito do(a) LOCADOR(A).</p>

        <h2>Cláusula 7ª – Da rescisão</h2>
        <p>Se o(a) LOCATÁRIO(A) devolver o imóvel antes do fim do prazo, pagará multa equivalente a 3 (três) aluguéis, proporcional ao tempo restante do contrato, nos termos do art. 4º da Lei nº 8.245/1991.</p>

        <h2>Cláusula 8ª – Do foro</h2>
        <p>Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer dúvidas oriundas deste contrato.</p>

        <p>E, por estarem assim justas e contratadas, as partes assinam o presente instrumento eletronicamente.</p>
        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("locador.nome", "Nome do locador"),
            Doc("locador.documento", "CPF/CNPJ do locador"),
            Text("locador.endereco", "Endereço do locador"),
            Text("locatario.nome", "Nome do locatário"),
            Doc("locatario.documento", "CPF/CNPJ do locatário"),
            Text("locatario.endereco", "Endereço atual do locatário"),
            Text("imovel.endereco", "Endereço do imóvel alugado"),
            Number("prazo.meses", "Prazo em meses"),
            Date("prazo.inicio", "Início da locação"),
            Money("aluguel.valor", "Valor do aluguel"),
            Number("aluguel.vencimento", "Dia do vencimento"),
            Text("reajuste.indice", "Índice de reajuste (ex.: IPCA)"),
            Text("garantia.descricao", "Garantia (ex.: caução de 3 aluguéis)"),
            Text("foro.cidade", "Cidade do foro"),
            Text("assinatura.cidade", "Cidade da assinatura"),
            Date("assinatura.data", "Data da assinatura"),
        ],
        [
            new("Locador", SignerAction.Sign, 1),
            new("Locatário", SignerAction.Sign, 2),
        ]);
}

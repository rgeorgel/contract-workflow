namespace Contratos.Application.Templates;

public static partial class TemplateStarters
{
    private static IEnumerable<TemplateStarter> Advocacia() =>
    [
        HonorariosAdvocaticios(),
        ProcuracaoAdJudicia(),
        DeclaracaoHipossuficiencia(),
        AcordoExtrajudicial(),
    ];

    /// <summary>
    /// Honorários advocatícios. Chaves e rótulos pensados para o Causify preencher sozinho:
    /// <c>contratante.*</c> é o cliente, <c>contratada.*</c> e <c>advogado.*</c> são o escritório.
    /// Evite "comarca" e "valor de cada parcela" nos rótulos: confundem a sugestão de fonte de lá.
    /// As variáveis opcionais ficam em trechos <c>{{#se}}</c>: sem valor, a frase some do documento.
    /// </summary>
    private static TemplateStarter HonorariosAdvocaticios() => new(
        "honorarios-advocaticios", "advocacia",
        "Prestação de serviços advocatícios (honorários)",
        "Contrato de honorários com objeto, valor, forma de pagamento, multa, juros, êxito, sucumbência, LGPD e testemunhas.",
        """
        <h1>Contrato de Prestação de Serviços Advocatícios</h1>
        <p><strong>CONTRATANTE:</strong> {{contratante.nome}}{{#se contratante.qualificacao}}, {{contratante.qualificacao}}{{/se}}, inscrito(a) no CPF/CNPJ sob o nº {{contratante.documento}}, com endereço em {{contratante.endereco}}{{#se contratante.email}}, e-mail {{contratante.email}}{{/se}}.</p>
        <p><strong>CONTRATADA:</strong> {{contratada.nome}}{{#se contratada.cnpj}}, inscrita no CNPJ sob o nº {{contratada.cnpj}}{{/se}}, com sede em {{contratada.endereco}}, neste ato representada por {{advogado.nome}}, inscrito(a) na OAB sob o nº {{advogado.oab}}.</p>
        <p>As partes acima identificadas celebram o presente contrato, que se rege pelas cláusulas seguintes.</p>

        <h2>Cláusula 1ª – Do objeto</h2>
        <p>1.1. A CONTRATADA prestará ao CONTRATANTE os seguintes serviços jurídicos: {{objeto}}.</p>
        <p>1.2. Os serviços terão início em {{contrato.inicio}} e abrangem a atuação até a decisão final na instância em que a causa estiver, salvo disposição diversa no objeto. Recursos a tribunais superiores, ações autônomas, execuções e incidentes não previstos no objeto dependem de ajuste à parte.</p>
        {{#se processo.numero}}<p>1.3. Processo relacionado: {{processo.numero}}.</p>{{/se}}

        <h2>Cláusula 2ª – Dos honorários contratuais</h2>
        <p>2.1. Pelos serviços, o CONTRATANTE pagará à CONTRATADA honorários de {{valor.total}}.</p>
        <p>2.2. Forma de pagamento: {{pagamento.forma}}.{{#se valor.entrada}} Entrada de {{valor.entrada}}.{{/se}}{{#se parcelas.quantidade}} Pagamento em {{parcelas.quantidade}} parcela(s){{#se pagamento.primeiro_vencimento}}, com o primeiro vencimento em {{pagamento.primeiro_vencimento}} e as demais nos meses subsequentes, no mesmo dia{{/se}}.{{/se}}</p>
        <p>2.3. Em caso de atraso, incidirão multa de {{multa.percentual}}% sobre o valor em atraso e juros de mora de {{juros.percentual}}% ao mês, calculados pro rata die, além de correção monetária.</p>
        {{#se exito.percentual}}<p>2.4. Honorários de êxito: {{exito.percentual}}% sobre o proveito econômico obtido pelo CONTRATANTE, devidos no recebimento.</p>{{/se}}

        <h2>Cláusula 3ª – Dos honorários de sucumbência</h2>
        <p>3.1. Os honorários de sucumbência pertencem exclusivamente à CONTRATADA (art. 23 da Lei nº 8.906/1994) e não se compensam com os honorários contratuais.</p>

        <h2>Cláusula 4ª – Das despesas</h2>
        <p>4.1. Custas, emolumentos, perícias, diligências, cópias e deslocamentos fora da comarca correm por conta do CONTRATANTE, mediante prévia comunicação e comprovação.</p>

        <h2>Cláusula 5ª – Das obrigações das partes</h2>
        <p>5.1. A CONTRATADA atuará com zelo e diligência, mantendo o CONTRATANTE informado do andamento, sem garantia de resultado, por se tratar de obrigação de meio.</p>
        <p>5.2. O CONTRATANTE fornecerá documentos e informações verdadeiros e completos, no prazo solicitado, e manterá seus dados de contato atualizados.</p>

        <h2>Cláusula 6ª – Da rescisão e da revogação do mandato</h2>
        <p>6.1. Qualquer das partes poderá encerrar este contrato mediante comunicação por escrito.</p>
        <p>6.2. Na revogação do mandato ou rescisão sem culpa da CONTRATADA, serão devidos os honorários vencidos e os proporcionais aos serviços já prestados{{#se exito.percentual}}, além dos de êxito, se o proveito vier a ser obtido em razão do trabalho realizado{{/se}}.</p>

        <h2>Cláusula 7ª – Da proteção de dados</h2>
        <p>7.1. A CONTRATADA tratará os dados pessoais do CONTRATANTE exclusivamente para a execução deste contrato e o cumprimento de obrigações legais, nos termos da Lei nº 13.709/2018 (LGPD), com sigilo profissional.</p>

        <h2>Cláusula 8ª – Da assinatura eletrônica</h2>
        <p>8.1. As partes reconhecem a validade da assinatura eletrônica deste instrumento, nos termos do art. 10, § 2º, da MP nº 2.200-2/2001 e da Lei nº 14.063/2020, e do registro de evidências (data, hora, IP e autenticação) emitido pela plataforma de assinatura.</p>

        <h2>Cláusula 9ª – Do foro</h2>
        <p>9.1. Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer questões oriundas deste contrato.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>

        <p>_________________________________________<br>CONTRATANTE: {{contratante.nome}}</p>
        <p>_________________________________________<br>CONTRATADA: {{contratada.nome}}<br>{{advogado.nome}} – OAB {{advogado.oab}}</p>
        <p><strong>TESTEMUNHAS:</strong></p>
        <p>1. _______________________________________</p>
        <p>2. _______________________________________</p>
        """,
        [
            // Partes
            Text("contratante.nome", "Nome do contratante"),
            Doc("contratante.documento", "CPF ou CNPJ do contratante"),
            Text("contratante.qualificacao", "Qualificação do contratante (nacionalidade, estado civil, profissão)", required: false),
            Text("contratante.endereco", "Endereço completo do contratante"),
            Text("contratante.email", "E-mail do contratante", required: false),
            Text("contratada.nome", "Nome do escritório (contratada)"),
            Doc("contratada.cnpj", "CNPJ do escritório (contratada)", required: false),
            Text("contratada.endereco", "Endereço do escritório"),
            Text("advogado.nome", "Nome do advogado responsável"),
            Text("advogado.oab", "Inscrição na OAB do advogado (nº/UF)"),
            // Objeto
            Text("objeto", "Serviço contratado (objeto)"),
            Text("processo.numero", "Número do processo, se já houver", required: false),
            Date("contrato.inicio", "Início da prestação dos serviços"),
            // Honorários e pagamento
            Money("valor.total", "Valor total dos honorários"),
            Text("pagamento.forma", "Forma de pagamento"),
            Money("valor.entrada", "Valor da entrada", required: false),
            Number("parcelas.quantidade", "Quantidade de parcelas", required: false),
            Date("pagamento.primeiro_vencimento", "Vencimento da 1ª parcela", required: false),
            Number("multa.percentual", "Multa por atraso (%)"),
            Number("juros.percentual", "Juros de mora ao mês (%)"),
            Number("exito.percentual", "Honorários de êxito (% do proveito econômico)", required: false),
            // Fechamento
            .. Forum(),
        ],
        [Signs("Contratante", 1), Signs("Contratada", 2), Witness(3), Witness(4)]);

    private static TemplateStarter ProcuracaoAdJudicia() => new(
        "procuracao-ad-judicia", "advocacia",
        "Procuração ad judicia et extra",
        "Outorga poderes gerais para o foro e poderes especiais (acordo, quitação, renúncia), com substabelecimento.",
        """
        <h1>Procuração Ad Judicia et Extra</h1>
        <p><strong>OUTORGANTE:</strong> {{outorgante.nome}}{{#se outorgante.qualificacao}}, {{outorgante.qualificacao}}{{/se}}, inscrito(a) no CPF/CNPJ sob o nº {{outorgante.documento}}, residente ou com sede em {{outorgante.endereco}}.</p>
        <p><strong>OUTORGADO(S):</strong> {{advogado.nome}}, advogado(a) inscrito(a) na OAB sob o nº {{advogado.oab}}{{#se escritorio.nome}}, integrante de {{escritorio.nome}}{{/se}}, com escritório em {{advogado.endereco}}, onde recebe intimações.</p>

        <h2>Poderes</h2>
        <p>Pelo presente instrumento, o(a) OUTORGANTE nomeia e constitui o(s) OUTORGADO(S) seu(s) bastante(s) procurador(es), a quem confere os poderes da cláusula <em>ad judicia et extra</em> para o foro em geral, em qualquer juízo, instância ou tribunal, podendo propor as ações competentes e defendê-lo(a) nas contrárias, seguindo umas e outras até decisão final, usando os recursos legais e acompanhando-os, conforme o art. 105 do Código de Processo Civil.</p>
        <p>Confere ainda poderes especiais para confessar, reconhecer a procedência do pedido, transigir, desistir, renunciar ao direito sobre o qual se funda a ação, receber, dar quitação, firmar compromisso e assinar declaração de hipossuficiência econômica, bem como para substabelecer, com ou sem reserva de poderes.</p>

        <h2>Finalidade</h2>
        <p>Especialmente para: {{finalidade}}.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("outorgante.nome", "Nome do outorgante (cliente)"),
            Text("outorgante.qualificacao", "Qualificação do outorgante (nacionalidade, estado civil, profissão)", required: false),
            Doc("outorgante.documento", "CPF ou CNPJ do outorgante"),
            Text("outorgante.endereco", "Endereço do outorgante"),
            Text("advogado.nome", "Nome do advogado outorgado"),
            Text("advogado.oab", "Inscrição na OAB do advogado (nº/UF)"),
            Text("escritorio.nome", "Nome do escritório", required: false),
            Text("advogado.endereco", "Endereço do escritório"),
            Text("finalidade", "Finalidade (ex.: ação de cobrança contra Fulano)"),
            .. Closing(),
        ],
        [Signs("Outorgante", 1)]);

    private static TemplateStarter DeclaracaoHipossuficiencia() => new(
        "declaracao-hipossuficiencia", "advocacia",
        "Declaração de hipossuficiência",
        "Declaração do cliente para pedir a gratuidade da justiça (art. 99 do CPC).",
        """
        <h1>Declaração de Hipossuficiência Econômica</h1>
        <p>Eu, {{declarante.nome}}{{#se declarante.qualificacao}}, {{declarante.qualificacao}}{{/se}}, inscrito(a) no CPF sob o nº {{declarante.cpf}}, residente em {{declarante.endereco}}, DECLARO, para os fins dos arts. 98 e 99 do Código de Processo Civil, que não tenho condições de arcar com as custas, as despesas processuais e os honorários advocatícios sem prejuízo do meu próprio sustento e do de minha família.</p>
        <p>Declaro que minha renda mensal é de aproximadamente {{declarante.renda}} e que tenho {{declarante.dependentes}} dependente(s).</p>
        <p>Declaro, por fim, estar ciente de que a falsidade desta declaração pode configurar crime (art. 299 do Código Penal) e sujeitar-me ao pagamento de até o décuplo das despesas processuais (art. 100, parágrafo único, do CPC).</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("declarante.nome", "Nome do declarante"),
            Text("declarante.qualificacao", "Qualificação do declarante (nacionalidade, estado civil, profissão)", required: false),
            Doc("declarante.cpf", "CPF do declarante"),
            Text("declarante.endereco", "Endereço do declarante"),
            Money("declarante.renda", "Renda mensal aproximada"),
            Number("declarante.dependentes", "Número de dependentes"),
            .. Closing(),
        ],
        [Signs("Declarante", 1)]);

    private static TemplateStarter AcordoExtrajudicial() => new(
        "acordo-extrajudicial", "advocacia",
        "Termo de acordo extrajudicial",
        "Encerra um conflito sem processo, com valor, parcelas, multa por descumprimento e quitação.",
        """
        <h1>Termo de Acordo Extrajudicial</h1>
        <p><strong>PRIMEIRO ACORDANTE:</strong> {{parte1.nome}}, inscrito(a) no CPF/CNPJ sob o nº {{parte1.documento}}, com endereço em {{parte1.endereco}}.</p>
        <p><strong>SEGUNDO ACORDANTE:</strong> {{parte2.nome}}, inscrito(a) no CPF/CNPJ sob o nº {{parte2.documento}}, com endereço em {{parte2.endereco}}.</p>
        {{#se advogados}}<p>Assistidos por seus advogados: {{advogados}}.</p>{{/se}}

        <h2>Cláusula 1ª – Do conflito</h2>
        <p>1.1. As partes divergem a respeito de {{conflito.descricao}} e, para evitar um processo judicial, resolvem pôr fim à controvérsia mediante concessões mútuas, nos termos dos arts. 840 e seguintes do Código Civil.</p>

        <h2>Cláusula 2ª – Do acordo</h2>
        <p>2.1. O SEGUNDO ACORDANTE pagará ao PRIMEIRO ACORDANTE o valor total de {{acordo.valor}}, em {{acordo.parcelas}} parcela(s), vencendo-se a primeira em {{acordo.primeiro_vencimento}}, por meio de {{pagamento.forma}}.</p>
        {{#se acordo.obrigacoes}}<p>2.2. As partes assumem ainda as seguintes obrigações: {{acordo.obrigacoes}}.</p>{{/se}}

        <h2>Cláusula 3ª – Do descumprimento</h2>
        <p>3.1. O atraso de qualquer parcela por mais de 10 (dez) dias implicará o vencimento antecipado do saldo, acrescido de multa de {{multa.percentual}}% e juros de 1% (um por cento) ao mês, podendo o PRIMEIRO ACORDANTE executar este termo.</p>

        <h2>Cláusula 4ª – Da quitação</h2>
        <p>4.1. Cumprido o acordo, as partes dão-se mútua, plena e irrevogável quitação quanto ao conflito descrito na Cláusula 1ª, nada mais tendo a reclamar uma da outra a esse respeito.</p>
        <p>4.2. Cada parte arcará com os honorários de seus advogados. Este termo, assinado por duas testemunhas, é título executivo extrajudicial (art. 784, III, do CPC) e pode ser levado à homologação judicial a pedido de qualquer das partes.</p>

        <h2>Cláusula 5ª – Do foro</h2>
        <p>Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer dúvidas oriundas deste acordo.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("parte1.nome", "Nome do primeiro acordante (quem recebe)"),
            Doc("parte1.documento", "CPF/CNPJ do primeiro acordante"),
            Text("parte1.endereco", "Endereço do primeiro acordante"),
            Text("parte2.nome", "Nome do segundo acordante (quem paga)"),
            Doc("parte2.documento", "CPF/CNPJ do segundo acordante"),
            Text("parte2.endereco", "Endereço do segundo acordante"),
            Text("advogados", "Advogados das partes, com OAB", required: false),
            Text("conflito.descricao", "Descrição do conflito"),
            Money("acordo.valor", "Valor total do acordo"),
            Number("acordo.parcelas", "Quantidade de parcelas"),
            Date("acordo.primeiro_vencimento", "Vencimento da 1ª parcela"),
            Text("pagamento.forma", "Forma de pagamento"),
            Text("acordo.obrigacoes", "Outras obrigações do acordo", required: false),
            Number("multa.percentual", "Multa por descumprimento (%)"),
            .. Forum(),
        ],
        [Signs("Primeiro acordante", 1), Signs("Segundo acordante", 2), Witness(3), Witness(4)]);
}

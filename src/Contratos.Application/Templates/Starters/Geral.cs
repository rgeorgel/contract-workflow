namespace Contratos.Application.Templates;

public static partial class TemplateStarters
{
    private static IEnumerable<TemplateStarter> Geral() =>
    [
        PrestacaoDeServicos(),
        Confidencialidade(),
        Distrato(),
        ConfissaoDeDivida(),
    ];

    private static TemplateStarter PrestacaoDeServicos() => new(
        "prestacao-servicos", "geral",
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
            .. Forum(),
        ],
        [Signs("Contratante", 1), Signs("Contratado", 2)]);

    private static TemplateStarter Confidencialidade() => new(
        "confidencialidade", "geral",
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
            .. Forum(),
        ],
        [Signs("Primeira parte", 1), Signs("Segunda parte", 2)]);

    private static TemplateStarter Distrato() => new(
        "distrato", "geral",
        "Distrato (encerramento amigável de contrato)",
        "Encerra um contrato em vigor de comum acordo, com acerto final de valores e quitação recíproca.",
        """
        <h1>Instrumento Particular de Distrato</h1>
        <p><strong>PRIMEIRA PARTE:</strong> {{parte1.nome}}, inscrita no CPF/CNPJ sob o nº {{parte1.documento}}, com endereço em {{parte1.endereco}}.</p>
        <p><strong>SEGUNDA PARTE:</strong> {{parte2.nome}}, inscrita no CPF/CNPJ sob o nº {{parte2.documento}}, com endereço em {{parte2.endereco}}.</p>
        <p>As partes, de comum acordo e nos termos do art. 472 do Código Civil, resolvem distratar o contrato abaixo identificado, mediante as cláusulas seguintes.</p>

        <h2>Cláusula 1ª – Do contrato distratado</h2>
        <p>1.1. É objeto deste distrato o contrato de {{contrato.descricao}}, firmado entre as partes em {{contrato.data}} (“Contrato”).</p>
        <p>1.2. O Contrato fica encerrado a partir de {{distrato.data_efeito}}, deixando de produzir efeitos a partir dessa data, ressalvado o disposto neste instrumento.</p>

        <h2>Cláusula 2ª – Do acerto final</h2>
        {{#se acerto.valor}}<p>2.1. Para o encerramento, {{acerto.devedor}} pagará à outra parte o valor de {{acerto.valor}}, da seguinte forma: {{acerto.forma}}.</p>{{/se}}
        <p>2.2. Cumprido o disposto neste instrumento, as partes dão uma à outra a mais ampla, geral e irrevogável quitação quanto às obrigações do Contrato, para nada mais reclamarem a qualquer título.</p>

        <h2>Cláusula 3ª – Das obrigações que permanecem</h2>
        <p>3.1. Permanecem válidas, mesmo após o distrato, as obrigações de sigilo, de proteção de dados pessoais e as demais que, por sua natureza, devam sobreviver ao término do Contrato.</p>
        <p>3.2. Cada parte devolverá à outra, em até 10 (dez) dias, os documentos, materiais e equipamentos que estiverem em seu poder e pertençam à outra.</p>

        <h2>Cláusula 4ª – Do foro</h2>
        <p>Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer dúvidas oriundas deste distrato.</p>

        <p>E, por estarem de acordo, as partes assinam o presente instrumento eletronicamente.</p>
        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("parte1.nome", "Nome da primeira parte"),
            Doc("parte1.documento", "CPF/CNPJ da primeira parte"),
            Text("parte1.endereco", "Endereço da primeira parte"),
            Text("parte2.nome", "Nome da segunda parte"),
            Doc("parte2.documento", "CPF/CNPJ da segunda parte"),
            Text("parte2.endereco", "Endereço da segunda parte"),
            Text("contrato.descricao", "Contrato encerrado (ex.: prestação de serviços de marketing)"),
            Date("contrato.data", "Data de assinatura do contrato original"),
            Date("distrato.data_efeito", "Data em que o contrato deixa de valer"),
            Money("acerto.valor", "Valor do acerto final", required: false),
            Text("acerto.devedor", "Quem paga o acerto (nome da parte)", required: false),
            Text("acerto.forma", "Forma de pagamento do acerto", required: false),
            .. Forum(),
        ],
        [Signs("Primeira parte", 1), Signs("Segunda parte", 2)]);

    private static TemplateStarter ConfissaoDeDivida() => new(
        "confissao-divida", "geral",
        "Confissão de dívida com parcelamento",
        "O devedor reconhece o débito e assume um plano de pagamento. Com duas testemunhas, vale como título executivo.",
        """
        <h1>Instrumento Particular de Confissão de Dívida</h1>
        <p><strong>CREDOR(A):</strong> {{credor.nome}}, inscrito(a) no CPF/CNPJ sob o nº {{credor.documento}}, com endereço em {{credor.endereco}}.</p>
        <p><strong>DEVEDOR(A):</strong> {{devedor.nome}}, inscrito(a) no CPF/CNPJ sob o nº {{devedor.documento}}, com endereço em {{devedor.endereco}}.</p>

        <h2>Cláusula 1ª – Da dívida</h2>
        <p>1.1. O(A) DEVEDOR(A) reconhece e confessa dever ao(à) CREDOR(A) a quantia líquida, certa e exigível de {{divida.valor}}, originada de {{divida.origem}}.</p>

        <h2>Cláusula 2ª – Do pagamento</h2>
        <p>2.1. A dívida será paga em {{parcelas.quantidade}} parcela(s) mensal(is) e sucessiva(s) de {{parcelas.valor}}, vencendo-se a primeira em {{parcelas.primeiro_vencimento}} e as demais no mesmo dia dos meses seguintes.</p>
        <p>2.2. Os pagamentos serão feitos por {{pagamento.forma}}, valendo o comprovante como recibo.</p>

        <h2>Cláusula 3ª – Do atraso e do vencimento antecipado</h2>
        <p>3.1. O atraso de qualquer parcela sujeitará o(a) DEVEDOR(A) a multa de 2% (dois por cento), juros de mora de 1% (um por cento) ao mês e correção monetária pelo {{correcao.indice}}.</p>
        <p>3.2. O atraso superior a 30 (trinta) dias de qualquer parcela provocará o vencimento antecipado de todo o saldo, que poderá ser cobrado de imediato, inclusive por execução.</p>

        <h2>Cláusula 4ª – Do título executivo</h2>
        <p>4.1. Este instrumento, assinado pelas partes e por duas testemunhas, constitui título executivo extrajudicial, nos termos do art. 784, III, do Código de Processo Civil.</p>
        <p>4.2. A tolerância do(a) CREDOR(A) com qualquer atraso não altera as condições aqui ajustadas.</p>

        <h2>Cláusula 5ª – Do foro</h2>
        <p>Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer dúvidas oriundas deste instrumento.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("credor.nome", "Nome do credor"),
            Doc("credor.documento", "CPF/CNPJ do credor"),
            Text("credor.endereco", "Endereço do credor"),
            Text("devedor.nome", "Nome do devedor"),
            Doc("devedor.documento", "CPF/CNPJ do devedor"),
            Text("devedor.endereco", "Endereço do devedor"),
            Money("divida.valor", "Valor total da dívida"),
            Text("divida.origem", "Origem da dívida (ex.: serviços não pagos de 2025)"),
            Number("parcelas.quantidade", "Quantidade de parcelas"),
            Money("parcelas.valor", "Valor de cada parcela"),
            Date("parcelas.primeiro_vencimento", "Vencimento da 1ª parcela"),
            Text("pagamento.forma", "Forma de pagamento (ex.: Pix para a chave ...)"),
            Text("correcao.indice", "Índice de correção (ex.: IPCA)"),
            .. Forum(),
        ],
        [Signs("Credor", 1), Signs("Devedor", 2), Witness(3), Witness(4)]);
}

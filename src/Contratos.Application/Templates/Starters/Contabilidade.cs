namespace Contratos.Application.Templates;

public static partial class TemplateStarters
{
    private static IEnumerable<TemplateStarter> Contabilidade() =>
    [
        ServicosContabeis(),
        BpoFinanceiro(),
        CartaDeResponsabilidade(),
        TransferenciaDeResponsabilidade(),
    ];

    private static TemplateStarter ServicosContabeis() => new(
        "servicos-contabeis", "contabilidade",
        "Prestação de serviços contábeis",
        "Contrato escritório × cliente com escopo (fiscal, contábil, pessoal), honorários, 13º, prazos de entrega e responsabilidades.",
        """
        <h1>Contrato de Prestação de Serviços Contábeis</h1>
        <p><strong>CONTRATANTE:</strong> {{cliente.razao_social}}, inscrita no CNPJ sob o nº {{cliente.cnpj}}, com sede em {{cliente.endereco}}, neste ato representada por {{cliente.representante}}.</p>
        <p><strong>CONTRATADA:</strong> {{escritorio.nome}}, inscrita no CNPJ sob o nº {{escritorio.cnpj}} e no CRC sob o nº {{escritorio.crc}}, com sede em {{escritorio.endereco}}, tendo como responsável técnico(a) {{contador.nome}}, CRC nº {{contador.crc}}.</p>
        <p>As partes celebram o presente contrato, em conformidade com a NBC PG 01 do Conselho Federal de Contabilidade, mediante as cláusulas seguintes.</p>

        <h2>Cláusula 1ª – Do objeto</h2>
        <p>1.1. A CONTRATADA prestará à CONTRATANTE, no regime tributário {{cliente.regime}}, os seguintes serviços: {{servicos.escopo}}.</p>
        <p>1.2. Não estão incluídos, e serão orçados à parte: abertura, alteração ou encerramento de empresa, regularizações de períodos anteriores, defesa em fiscalizações, perícias, consultorias específicas e declarações de sócios não previstas acima.</p>

        <h2>Cláusula 2ª – Dos honorários</h2>
        <p>2.1. A CONTRATANTE pagará à CONTRATADA honorários mensais de {{honorarios.valor}}, com vencimento no dia {{honorarios.vencimento}} de cada mês, reajustados anualmente pelo {{reajuste.indice}}.</p>
        <p>2.2. No mês de dezembro será devida uma parcela adicional, de igual valor, referente aos serviços de encerramento do exercício e às obrigações anuais (13º honorário).</p>
        <p>2.3. O atraso sujeitará a CONTRATANTE a multa de 2% (dois por cento) e juros de 1% (um por cento) ao mês. Atrasos superiores a 60 (sessenta) dias autorizam a suspensão dos serviços, mediante aviso prévio.</p>

        <h2>Cláusula 3ª – Das obrigações da CONTRATANTE</h2>
        <p>3.1. Enviar à CONTRATADA, até o dia {{documentos.prazo}} do mês seguinte ao de competência, os documentos fiscais, extratos bancários, folha de ponto e demais informações necessárias, em ordem e sem rasuras.</p>
        <p>3.2. Responder pela veracidade dos documentos e informações fornecidos, e pelas consequências de sua entrega fora do prazo.</p>

        <h2>Cláusula 4ª – Das obrigações da CONTRATADA</h2>
        <p>4.1. Executar os serviços com zelo e observância das normas profissionais, entregar as obrigações acessórias nos prazos legais quando recebida a documentação no prazo, e manter sigilo sobre as informações da CONTRATANTE.</p>
        <p>4.2. A CONTRATADA responde pelas multas decorrentes de erro ou atraso que lhe sejam exclusivamente imputáveis.</p>

        <h2>Cláusula 5ª – Da proteção de dados</h2>
        <p>5.1. A CONTRATADA tratará os dados pessoais de sócios e empregados da CONTRATANTE apenas para executar este contrato e cumprir obrigações legais, nos termos da Lei nº 13.709/2018 (LGPD).</p>

        <h2>Cláusula 6ª – Da vigência e da rescisão</h2>
        <p>6.1. Este contrato vigora a partir de {{contrato.inicio}}, por prazo indeterminado, e pode ser rescindido por qualquer das partes com aviso prévio de {{aviso.dias}} dias, durante o qual os serviços e os honorários seguem normalmente.</p>
        <p>6.2. Na rescisão, a CONTRATADA devolverá os documentos da CONTRATANTE mediante termo de entrega e transferência de responsabilidade técnica.</p>

        <h2>Cláusula 7ª – Do foro</h2>
        <p>Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer dúvidas oriundas deste contrato.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("cliente.razao_social", "Razão social do cliente"),
            Doc("cliente.cnpj", "CNPJ do cliente"),
            Text("cliente.endereco", "Endereço do cliente"),
            Text("cliente.representante", "Representante legal do cliente"),
            Text("escritorio.nome", "Nome do escritório contábil"),
            Doc("escritorio.cnpj", "CNPJ do escritório"),
            Text("escritorio.crc", "CRC do escritório"),
            Text("escritorio.endereco", "Endereço do escritório"),
            Text("contador.nome", "Contador responsável técnico"),
            Text("contador.crc", "CRC do contador"),
            Text("cliente.regime", "Regime tributário (ex.: Simples Nacional)"),
            Text("servicos.escopo", "Serviços incluídos (ex.: escrituração contábil e fiscal, folha de até 5 empregados)"),
            Money("honorarios.valor", "Honorários mensais"),
            Number("honorarios.vencimento", "Dia do vencimento dos honorários"),
            Text("reajuste.indice", "Índice de reajuste (ex.: INPC)"),
            Number("documentos.prazo", "Dia limite para envio dos documentos"),
            Date("contrato.inicio", "Início dos serviços"),
            Number("aviso.dias", "Aviso prévio para rescindir (dias)"),
            .. Forum(),
        ],
        [Signs("Contratante", 1), Signs("Contratada", 2)]);

    private static TemplateStarter BpoFinanceiro() => new(
        "bpo-financeiro", "contabilidade",
        "BPO financeiro",
        "Terceirização da rotina financeira: contas a pagar e receber, conciliação, relatórios e acesso bancário.",
        """
        <h1>Contrato de Prestação de Serviços de BPO Financeiro</h1>
        <p><strong>CONTRATANTE:</strong> {{cliente.razao_social}}, inscrita no CNPJ sob o nº {{cliente.cnpj}}, com sede em {{cliente.endereco}}, neste ato representada por {{cliente.representante}}.</p>
        <p><strong>CONTRATADA:</strong> {{escritorio.nome}}, inscrita no CNPJ sob o nº {{escritorio.cnpj}}, com sede em {{escritorio.endereco}}.</p>

        <h2>Cláusula 1ª – Do objeto</h2>
        <p>1.1. A CONTRATADA executará, de forma terceirizada, as seguintes rotinas financeiras da CONTRATANTE: {{servicos.escopo}}.</p>
        <p>1.2. Os pagamentos serão agendados pela CONTRATADA e liberados pela CONTRATANTE, que mantém a decisão final sobre cada pagamento e sobre o uso dos seus recursos.</p>

        <h2>Cláusula 2ª – Dos acessos</h2>
        <p>2.1. A CONTRATANTE concederá à CONTRATADA acesso aos sistemas e às contas bancárias apenas no perfil necessário aos serviços, preferencialmente sem poder de liberar pagamentos, podendo revogá-lo a qualquer tempo.</p>
        <p>2.2. A CONTRATADA guardará sigilo sobre senhas, dados bancários e informações financeiras, e não as compartilhará com terceiros.</p>

        <h2>Cláusula 3ª – Dos relatórios</h2>
        <p>3.1. A CONTRATADA enviará à CONTRATANTE, até o dia {{relatorio.dia}} de cada mês, relatório com fluxo de caixa realizado, contas em aberto e conciliação bancária do mês anterior.</p>

        <h2>Cláusula 4ª – Do preço</h2>
        <p>4.1. A CONTRATANTE pagará mensalmente {{honorarios.valor}}, com vencimento no dia {{honorarios.vencimento}}, considerando um volume de até {{volume.lancamentos}} lançamentos por mês. O volume excedente será cobrado à parte, conforme tabela previamente aprovada.</p>

        <h2>Cláusula 5ª – Das responsabilidades</h2>
        <p>5.1. A CONTRATANTE responde pela veracidade das informações e documentos enviados e pela existência de saldo para os pagamentos. A CONTRATADA responde por erros na execução das rotinas que lhe forem imputáveis.</p>

        <h2>Cláusula 6ª – Da proteção de dados</h2>
        <p>6.1. As partes cumprirão a Lei nº 13.709/2018 (LGPD), tratando os dados pessoais apenas para a execução deste contrato.</p>

        <h2>Cláusula 7ª – Da vigência</h2>
        <p>7.1. Este contrato vigora por prazo indeterminado a partir de {{contrato.inicio}} e pode ser rescindido com aviso prévio de 30 (trinta) dias. Na rescisão, a CONTRATADA devolverá os acessos e entregará os relatórios do último mês trabalhado.</p>

        <h2>Cláusula 8ª – Do foro</h2>
        <p>Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer dúvidas oriundas deste contrato.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("cliente.razao_social", "Razão social do cliente"),
            Doc("cliente.cnpj", "CNPJ do cliente"),
            Text("cliente.endereco", "Endereço do cliente"),
            Text("cliente.representante", "Representante legal do cliente"),
            Text("escritorio.nome", "Nome da empresa de BPO"),
            Doc("escritorio.cnpj", "CNPJ da empresa de BPO"),
            Text("escritorio.endereco", "Endereço da empresa de BPO"),
            Text("servicos.escopo", "Rotinas incluídas (ex.: contas a pagar, emissão de boletos, conciliação)"),
            Number("relatorio.dia", "Dia de entrega do relatório mensal"),
            Money("honorarios.valor", "Valor mensal"),
            Number("honorarios.vencimento", "Dia do vencimento"),
            Number("volume.lancamentos", "Lançamentos incluídos por mês"),
            Date("contrato.inicio", "Início dos serviços"),
            .. Forum(),
        ],
        [Signs("Contratante", 1), Signs("Contratada", 2)]);

    private static TemplateStarter CartaDeResponsabilidade() => new(
        "carta-responsabilidade", "contabilidade",
        "Carta de responsabilidade da administração",
        "Declaração anual dos administradores sobre as informações entregues para as demonstrações contábeis (ITG 1000).",
        """
        <h1>Carta de Responsabilidade da Administração</h1>
        <p>{{cidade}}, {{assinatura.data}}.</p>
        <p>À {{escritorio.nome}}<br>CRC nº {{escritorio.crc}}</p>
        <p>Prezados,</p>
        <p>Declaramos, para os devidos fins, como administradores e responsáveis legais pela empresa {{cliente.razao_social}}, CNPJ nº {{cliente.cnpj}}, que as informações relacionadas a seguir, referentes ao exercício encerrado em {{exercicio.data_encerramento}}, são completas e verdadeiras:</p>
        <ol>
          <li>os documentos e informações enviados para a escrituração contábil e fiscal representam todas as operações realizadas pela empresa no período, e estão em nosso poder e arquivados conforme a legislação;</li>
          <li>não houve fatos ou operações relevantes, como contingências, garantias prestadas, ações judiciais ou acordos, que não tenham sido informados à contabilidade;</li>
          <li>os saldos de caixa, bancos, estoques, contas a receber e a pagar informados correspondem à realidade em {{exercicio.data_encerramento}};</li>
          <li>não houve eventos subsequentes ao encerramento do exercício que exijam ajuste ou divulgação nas demonstrações contábeis;</li>
          <li>a empresa cumpriu as obrigações trabalhistas, previdenciárias e tributárias, ou informou à contabilidade as pendências existentes.</li>
        </ol>
        <p>Reconhecemos que somos responsáveis pelas informações acima e pelas demonstrações contábeis elaboradas a partir delas, nos termos da ITG 1000 e da NBC TG 1000 do Conselho Federal de Contabilidade.</p>
        <p>Atenciosamente,</p>
        <p>{{administrador.nome}}<br>{{administrador.cargo}}<br>CPF nº {{administrador.cpf}}</p>
        """,
        [
            Text("cidade", "Cidade"),
            Date("assinatura.data", "Data da carta"),
            Text("escritorio.nome", "Nome do escritório contábil"),
            Text("escritorio.crc", "CRC do escritório"),
            Text("cliente.razao_social", "Razão social da empresa"),
            Doc("cliente.cnpj", "CNPJ da empresa"),
            Date("exercicio.data_encerramento", "Data de encerramento do exercício"),
            Text("administrador.nome", "Nome do administrador"),
            Text("administrador.cargo", "Cargo do administrador (ex.: sócio-administrador)"),
            Doc("administrador.cpf", "CPF do administrador"),
        ],
        [Signs("Administrador", 1)]);

    private static TemplateStarter TransferenciaDeResponsabilidade() => new(
        "transferencia-responsabilidade", "contabilidade",
        "Termo de transferência de responsabilidade técnica",
        "Entrega de documentos e passagem da contabilidade do escritório anterior para o novo, com a última competência de cada um.",
        """
        <h1>Termo de Transferência de Responsabilidade Técnica e Entrega de Documentos</h1>
        <p><strong>EMPRESA:</strong> {{cliente.razao_social}}, inscrita no CNPJ sob o nº {{cliente.cnpj}}, neste ato representada por {{cliente.representante}}.</p>
        <p><strong>CONTADOR(A) ANTERIOR:</strong> {{anterior.nome}}, CRC nº {{anterior.crc}}.</p>
        <p><strong>NOVO(A) CONTADOR(A):</strong> {{novo.nome}}, CRC nº {{novo.crc}}.</p>

        <h2>1. Da transferência</h2>
        <p>1.1. A responsabilidade técnica pela contabilidade da EMPRESA passa do(a) CONTADOR(A) ANTERIOR para o(a) NOVO(A) CONTADOR(A). O(A) CONTADOR(A) ANTERIOR responde pelos serviços até a competência de {{competencia.ultima_anterior}}, e o(a) NOVO(A) CONTADOR(A) a partir da competência seguinte.</p>

        <h2>2. Dos documentos entregues</h2>
        <p>2.1. Nesta data, o(a) CONTADOR(A) ANTERIOR entrega os seguintes documentos e arquivos: {{documentos.lista}}.</p>
        {{#se documentos.pendencias}}<p>2.2. Ficam registradas as seguintes pendências: {{documentos.pendencias}}.</p>{{/se}}

        <h2>3. Das declarações</h2>
        <p>3.1. As partes declaram que a entrega se faz em atendimento à NBC PG 01, que os documentos acima foram conferidos por amostragem e que eventuais falhas de períodos anteriores continuam sob a responsabilidade de quem os elaborou.</p>
        <p>3.2. A EMPRESA declara estar ciente da transferência e autoriza o(a) NOVO(A) CONTADOR(A) a acessar os sistemas dos órgãos públicos em seu nome.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("cliente.razao_social", "Razão social da empresa"),
            Doc("cliente.cnpj", "CNPJ da empresa"),
            Text("cliente.representante", "Representante legal da empresa"),
            Text("anterior.nome", "Contador ou escritório anterior"),
            Text("anterior.crc", "CRC do contador anterior"),
            Text("novo.nome", "Novo contador ou escritório"),
            Text("novo.crc", "CRC do novo contador"),
            Text("competencia.ultima_anterior", "Última competência do anterior (ex.: 10/2026)"),
            Text("documentos.lista", "Documentos entregues (livros, balancetes, senhas, arquivos SPED)"),
            Text("documentos.pendencias", "Pendências conhecidas", required: false),
            .. Closing(),
        ],
        [Signs("Empresa", 1), Signs("Contador anterior", 2), Signs("Novo contador", 3)]);
}

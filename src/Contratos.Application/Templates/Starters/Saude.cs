namespace Contratos.Application.Templates;

public static partial class TemplateStarters
{
    private static IEnumerable<TemplateStarter> Saude() =>
    [
        ConsentimentoInformado(),
        PlanoDeTratamento(),
        UsoDeImagemClinica(),
    ];

    private static TemplateStarter ConsentimentoInformado() => new(
        "consentimento-procedimento", "saude",
        "Termo de consentimento livre e esclarecido",
        "O paciente declara ter sido informado do procedimento, riscos, alternativas e cuidados, e autoriza a sua realização.",
        """
        <h1>Termo de Consentimento Livre e Esclarecido</h1>
        <p><strong>PACIENTE:</strong> {{paciente.nome}}, inscrito(a) no CPF sob o nº {{paciente.cpf}}, nascido(a) em {{paciente.nascimento}}.</p>
        {{#se responsavel.nome}}<p><strong>RESPONSÁVEL LEGAL:</strong> {{responsavel.nome}}, inscrito(a) no CPF sob o nº {{responsavel.cpf}}.</p>{{/se}}
        <p><strong>PROFISSIONAL:</strong> {{profissional.nome}}, {{profissional.registro}}.</p>

        <h2>1. Procedimento</h2>
        <p>Autorizo a realização do seguinte procedimento: {{procedimento.nome}}, previsto para {{procedimento.data}}, no estabelecimento {{clinica.nome}}.</p>

        <h2>2. Informações recebidas</h2>
        <p>Declaro que recebi do(a) profissional, em linguagem clara, explicações sobre:</p>
        <ul>
          <li>a finalidade e a forma de realização do procedimento: {{procedimento.descricao}};</li>
          <li>os riscos e as possíveis complicações: {{procedimento.riscos}};</li>
          <li>as alternativas de tratamento existentes, inclusive a de não realizar o procedimento;</li>
          <li>os cuidados que devo seguir antes e depois: {{procedimento.cuidados}}.</li>
        </ul>
        <p>Tive a oportunidade de fazer perguntas, que foram respondidas de forma satisfatória. Compreendo que a medicina e a odontologia não são ciências exatas e que não há garantia de resultado.</p>

        <h2>3. Imprevistos</h2>
        <p>Autorizo o(a) profissional a adotar as medidas necessárias diante de situações imprevistas durante o procedimento, para preservar minha saúde e minha vida.</p>

        <h2>4. Revogação</h2>
        <p>Sei que posso revogar este consentimento a qualquer momento antes do início do procedimento, sem prejuízo do meu atendimento.</p>

        <h2>5. Dados de saúde</h2>
        <p>Autorizo o tratamento dos meus dados de saúde para a realização do procedimento e o registro no prontuário, nos termos do art. 11 da Lei nº 13.709/2018 (LGPD), com sigilo profissional.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("paciente.nome", "Nome do paciente"),
            Doc("paciente.cpf", "CPF do paciente"),
            Date("paciente.nascimento", "Data de nascimento do paciente"),
            Text("responsavel.nome", "Responsável legal (se menor ou incapaz)", required: false),
            Doc("responsavel.cpf", "CPF do responsável legal", required: false),
            Text("profissional.nome", "Nome do profissional"),
            Text("profissional.registro", "Registro profissional (ex.: CRM 12345/SP)"),
            Text("procedimento.nome", "Procedimento"),
            Date("procedimento.data", "Data prevista do procedimento"),
            Text("clinica.nome", "Clínica ou hospital"),
            Text("procedimento.descricao", "Como o procedimento é feito"),
            Text("procedimento.riscos", "Riscos e complicações possíveis"),
            Text("procedimento.cuidados", "Cuidados antes e depois"),
            .. Closing(),
        ],
        [Signs("Paciente", 1), Signs("Profissional", 2)]);

    private static TemplateStarter PlanoDeTratamento() => new(
        "plano-tratamento", "saude",
        "Contrato de tratamento (clínica ou consultório)",
        "Plano de tratamento com sessões, valor, parcelamento, faltas e remarcações, para odontologia, estética ou terapias.",
        """
        <h1>Contrato de Prestação de Serviços de Saúde</h1>
        <p><strong>CONTRATANTE (PACIENTE):</strong> {{paciente.nome}}, inscrito(a) no CPF sob o nº {{paciente.cpf}}, com endereço em {{paciente.endereco}}.</p>
        <p><strong>CONTRATADA:</strong> {{clinica.nome}}, inscrita no CPF/CNPJ sob o nº {{clinica.documento}}, com endereço em {{clinica.endereco}}, tendo como responsável técnico(a) {{profissional.nome}}, {{profissional.registro}}.</p>

        <h2>Cláusula 1ª – Do tratamento</h2>
        <p>1.1. A CONTRATADA realizará no(a) CONTRATANTE o seguinte plano de tratamento: {{tratamento.descricao}}, com previsão de {{tratamento.sessoes}} sessão(ões).</p>
        <p>1.2. O plano poderá ser alterado por necessidade clínica, mediante explicação ao(à) CONTRATANTE e novo orçamento, se houver mudança de valor.</p>

        <h2>Cláusula 2ª – Do valor</h2>
        <p>2.1. O valor total do tratamento é de {{valor.total}}, pago da seguinte forma: {{pagamento.forma}}.</p>
        <p>2.2. O atraso no pagamento sujeitará o(a) CONTRATANTE a multa de 2% (dois por cento) e juros de 1% (um por cento) ao mês, podendo a CONTRATADA suspender as sessões ainda não iniciadas até a regularização.</p>

        <h2>Cláusula 3ª – Das faltas e remarcações</h2>
        <p>3.1. Os horários devem ser desmarcados com pelo menos {{remarcacao.horas}} horas de antecedência. A falta sem aviso poderá ser cobrada como sessão realizada.</p>

        <h2>Cláusula 4ª – Das obrigações do(a) CONTRATANTE</h2>
        <p>4.1. Informar seu histórico de saúde, medicamentos e alergias com veracidade, comparecer às sessões e seguir as orientações recebidas, ciente de que o resultado depende também de sua colaboração.</p>

        <h2>Cláusula 5ª – Da desistência</h2>
        <p>5.1. Em caso de desistência, serão devidos os valores das sessões realizadas e dos materiais já adquiridos para o tratamento, devolvendo-se o que tiver sido pago a mais.</p>

        <h2>Cláusula 6ª – Do prontuário e dos dados</h2>
        <p>6.1. A CONTRATADA manterá prontuário do tratamento e tratará os dados de saúde do(a) CONTRATANTE com sigilo, nos termos da Lei nº 13.709/2018 (LGPD).</p>

        <h2>Cláusula 7ª – Do foro</h2>
        <p>Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer dúvidas oriundas deste contrato.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("paciente.nome", "Nome do paciente"),
            Doc("paciente.cpf", "CPF do paciente"),
            Text("paciente.endereco", "Endereço do paciente"),
            Text("clinica.nome", "Nome da clínica ou do profissional"),
            Doc("clinica.documento", "CPF/CNPJ da clínica"),
            Text("clinica.endereco", "Endereço da clínica"),
            Text("profissional.nome", "Profissional responsável"),
            Text("profissional.registro", "Registro profissional (ex.: CRO 12345/SP)"),
            Text("tratamento.descricao", "Plano de tratamento"),
            Number("tratamento.sessoes", "Número de sessões previstas"),
            Money("valor.total", "Valor total do tratamento"),
            Text("pagamento.forma", "Forma de pagamento"),
            Number("remarcacao.horas", "Antecedência para desmarcar (horas)"),
            .. Forum(),
        ],
        [Signs("Paciente", 1), Signs("Clínica", 2)]);

    private static TemplateStarter UsoDeImagemClinica() => new(
        "uso-imagem-saude", "saude",
        "Autorização de uso de imagem (antes e depois)",
        "O paciente autoriza fotos e vídeos do tratamento para prontuário, ensino ou divulgação, dentro das regras do conselho.",
        """
        <h1>Termo de Autorização de Uso de Imagem</h1>
        <p>Eu, {{paciente.nome}}, inscrito(a) no CPF sob o nº {{paciente.cpf}}, autorizo {{clinica.nome}} e o(a) profissional {{profissional.nome}}, {{profissional.registro}}, a registrar fotos e vídeos do meu tratamento ({{tratamento}}).</p>

        <h2>Finalidades autorizadas</h2>
        <p>As imagens poderão ser usadas para as seguintes finalidades: {{finalidades}}.</p>
        <p>Em qualquer uso fora do prontuário, meu nome não será divulgado{{#se ocultar.rosto}} e meu rosto será ocultado: {{ocultar.rosto}}{{/se}}. A divulgação observará as normas éticas do conselho profissional, sem promessa de resultado.</p>

        <h2>Condições</h2>
        <p>Esta autorização é gratuita, vale por {{vigencia.anos}} ano(s) e pode ser revogada por mim a qualquer momento, por escrito, deixando as imagens de ser usadas em novas publicações a partir da revogação.</p>
        <p>O tratamento das imagens segue a Lei nº 13.709/2018 (LGPD), e posso pedir acesso, correção ou exclusão dos meus dados a qualquer tempo.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("paciente.nome", "Nome do paciente"),
            Doc("paciente.cpf", "CPF do paciente"),
            Text("clinica.nome", "Nome da clínica"),
            Text("profissional.nome", "Nome do profissional"),
            Text("profissional.registro", "Registro profissional"),
            Text("tratamento", "Tratamento registrado"),
            Text("finalidades", "Finalidades (ex.: prontuário, aulas e eventos científicos, redes sociais da clínica)"),
            Text("ocultar.rosto", "Como o rosto será ocultado (deixe em branco se não for)", required: false),
            Number("vigencia.anos", "Validade da autorização (anos)"),
            .. Closing(),
        ],
        [Signs("Paciente", 1)]);
}

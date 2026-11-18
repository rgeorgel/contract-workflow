namespace Contratos.Application.Templates;

public static partial class TemplateStarters
{
    private static IEnumerable<TemplateStarter> Educacao() =>
    [
        ServicosEducacionais(),
        CursoLivre(),
        UsoDeImagemAluno(),
    ];

    private static TemplateStarter ServicosEducacionais() => new(
        "servicos-educacionais", "educacao",
        "Contrato de matrícula (prestação de serviços educacionais)",
        "Matrícula escolar com anuidade, parcelas, material, desistência e responsável financeiro (Lei nº 9.870/1999).",
        """
        <h1>Contrato de Prestação de Serviços Educacionais</h1>
        <p><strong>CONTRATADA:</strong> {{escola.nome}}, inscrita no CNPJ sob o nº {{escola.cnpj}}, com sede em {{escola.endereco}}.</p>
        <p><strong>CONTRATANTE (RESPONSÁVEL FINANCEIRO):</strong> {{responsavel.nome}}, inscrito(a) no CPF sob o nº {{responsavel.cpf}}, residente em {{responsavel.endereco}}.</p>
        <p><strong>ALUNO(A):</strong> {{aluno.nome}}, nascido(a) em {{aluno.nascimento}}.</p>

        <h2>Cláusula 1ª – Do objeto</h2>
        <p>1.1. A CONTRATADA prestará ao(à) ALUNO(A) os serviços educacionais correspondentes ao(à) {{curso.serie}}, no turno {{curso.turno}}, durante o ano letivo de {{ano.letivo}}, conforme o calendário escolar, o projeto pedagógico e o regimento escolar, que o(a) CONTRATANTE declara conhecer.</p>

        <h2>Cláusula 2ª – Da anuidade</h2>
        <p>2.1. Pelos serviços, o(a) CONTRATANTE pagará a anuidade de {{anuidade.valor}}, dividida em {{anuidade.parcelas}} parcelas mensais, com vencimento no dia {{anuidade.vencimento}} de cada mês, nos termos da Lei nº 9.870/1999.</p>
        {{#se desconto}}<p>2.2. Desconto concedido: {{desconto}}, válido para pagamentos até o vencimento.</p>{{/se}}
        <p>2.3. O atraso sujeitará o(a) CONTRATANTE a multa de 2% (dois por cento), juros de 1% (um por cento) ao mês e correção monetária. A inadimplência não autoriza a suspensão de provas, a retenção de documentos escolares ou a aplicação de penalidades pedagógicas, mas a CONTRATADA poderá recusar a rematrícula para o ano seguinte.</p>

        <h2>Cláusula 3ª – Do que não está incluído</h2>
        <p>3.1. Não estão incluídos na anuidade: material didático individual, uniforme, transporte, alimentação, atividades extracurriculares opcionais, passeios e segunda via de documentos.</p>

        <h2>Cláusula 4ª – Da desistência e da transferência</h2>
        <p>4.1. O(A) CONTRATANTE pode cancelar a matrícula a qualquer tempo, por escrito, ficando obrigado(a) ao pagamento das parcelas vencidas até o mês do pedido. Desistências antes do início das aulas darão direito à devolução de {{devolucao.percentual}}% do valor pago, retido o restante para cobrir despesas administrativas.</p>

        <h2>Cláusula 5ª – Da saúde e da segurança do(a) aluno(a)</h2>
        <p>5.1. O(A) CONTRATANTE informará à escola as condições de saúde, alergias, medicamentos e pessoas autorizadas a buscar o(a) ALUNO(A), mantendo esses dados atualizados.</p>

        <h2>Cláusula 6ª – Da proteção de dados</h2>
        <p>6.1. A CONTRATADA tratará os dados do(a) ALUNO(A) e do(a) CONTRATANTE para a execução deste contrato e o cumprimento de obrigações legais, observando o melhor interesse da criança e do adolescente (art. 14 da Lei nº 13.709/2018).</p>

        <h2>Cláusula 7ª – Do foro</h2>
        <p>Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer dúvidas oriundas deste contrato.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("escola.nome", "Nome da escola"),
            Doc("escola.cnpj", "CNPJ da escola"),
            Text("escola.endereco", "Endereço da escola"),
            Text("responsavel.nome", "Nome do responsável financeiro"),
            Doc("responsavel.cpf", "CPF do responsável financeiro"),
            Text("responsavel.endereco", "Endereço do responsável"),
            Text("aluno.nome", "Nome do aluno"),
            Date("aluno.nascimento", "Data de nascimento do aluno"),
            Text("curso.serie", "Série ou curso (ex.: 5º ano do Ensino Fundamental)"),
            Text("curso.turno", "Turno (ex.: matutino)"),
            Text("ano.letivo", "Ano letivo (ex.: 2027)"),
            Money("anuidade.valor", "Valor da anuidade"),
            Number("anuidade.parcelas", "Número de parcelas"),
            Number("anuidade.vencimento", "Dia do vencimento"),
            Text("desconto", "Desconto (ex.: 5% para pagamento até o dia 5)", required: false),
            Number("devolucao.percentual", "Devolução se desistir antes das aulas (%)"),
            .. Forum(),
        ],
        [Signs("Responsável financeiro", 1), Signs("Escola", 2)]);

    private static TemplateStarter CursoLivre() => new(
        "curso-livre", "educacao",
        "Curso livre, treinamento ou mentoria",
        "Inscrição em curso presencial ou online, com conteúdo, carga horária, acesso, certificado e direito de arrependimento.",
        """
        <h1>Contrato de Prestação de Serviços – Curso</h1>
        <p><strong>CONTRATADA:</strong> {{empresa.nome}}, inscrita no CPF/CNPJ sob o nº {{empresa.documento}}, com endereço em {{empresa.endereco}}.</p>
        <p><strong>ALUNO(A):</strong> {{aluno.nome}}, inscrito(a) no CPF sob o nº {{aluno.cpf}}, e-mail {{aluno.email}}.</p>

        <h2>Cláusula 1ª – Do curso</h2>
        <p>1.1. A CONTRATADA oferecerá ao(à) ALUNO(A) o curso {{curso.nome}}, na modalidade {{curso.modalidade}}, com carga horária de {{curso.carga_horaria}} horas e o seguinte conteúdo: {{curso.conteudo}}.</p>
        <p>1.2. As aulas começam em {{curso.inicio}}. {{#se curso.acesso_meses}}O acesso ao conteúdo gravado fica disponível por {{curso.acesso_meses}} meses a partir da liberação.{{/se}}</p>

        <h2>Cláusula 2ª – Do investimento</h2>
        <p>2.1. O(A) ALUNO(A) pagará {{valor.total}}, da seguinte forma: {{pagamento.forma}}.</p>

        <h2>Cláusula 3ª – Do certificado</h2>
        <p>3.1. Será emitido certificado de participação ao(à) ALUNO(A) que tiver frequência ou conclusão mínima de {{certificado.frequencia}}% do curso. Trata-se de curso livre, que não depende de autorização do MEC.</p>

        <h2>Cláusula 4ª – Do uso do conteúdo</h2>
        <p>4.1. O acesso é pessoal e intransferível. É proibido gravar, copiar, compartilhar ou revender aulas e materiais, que são protegidos pela Lei nº 9.610/1998, sob pena de cancelamento do acesso sem devolução e de responsabilização.</p>

        <h2>Cláusula 5ª – Do arrependimento e do cancelamento</h2>
        <p>5.1. Na contratação pela internet, o(a) ALUNO(A) pode desistir em até 7 (sete) dias da compra, com devolução integral (art. 49 do Código de Defesa do Consumidor).</p>
        <p>5.2. Após esse prazo, o cancelamento dará direito à devolução proporcional ao conteúdo ainda não disponibilizado, descontada multa de {{cancelamento.multa_percentual}}% sobre o saldo.</p>

        <h2>Cláusula 6ª – Do foro</h2>
        <p>Fica eleito o foro do domicílio do(a) ALUNO(A) para dirimir quaisquer dúvidas oriundas deste contrato.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("empresa.nome", "Nome da escola ou do instrutor"),
            Doc("empresa.documento", "CPF/CNPJ da contratada"),
            Text("empresa.endereco", "Endereço da contratada"),
            Text("aluno.nome", "Nome do aluno"),
            Doc("aluno.cpf", "CPF do aluno"),
            Text("aluno.email", "E-mail do aluno"),
            Text("curso.nome", "Nome do curso"),
            Text("curso.modalidade", "Modalidade (presencial, online ao vivo, gravado)"),
            Number("curso.carga_horaria", "Carga horária (horas)"),
            Text("curso.conteudo", "Conteúdo resumido"),
            Date("curso.inicio", "Início das aulas"),
            Number("curso.acesso_meses", "Meses de acesso ao conteúdo gravado", required: false),
            Money("valor.total", "Valor do curso"),
            Text("pagamento.forma", "Forma de pagamento"),
            Number("certificado.frequencia", "Frequência mínima para certificado (%)"),
            Number("cancelamento.multa_percentual", "Multa de cancelamento após 7 dias (%)"),
            .. Closing(),
        ],
        [Signs("Aluno", 1), Signs("Contratada", 2)]);

    private static TemplateStarter UsoDeImagemAluno() => new(
        "uso-imagem-aluno", "educacao",
        "Autorização de uso de imagem de aluno",
        "O responsável autoriza fotos e vídeos do aluno em atividades escolares, com finalidades e canais definidos.",
        """
        <h1>Termo de Autorização de Uso de Imagem e Voz</h1>
        <p>Eu, {{responsavel.nome}}, inscrito(a) no CPF sob o nº {{responsavel.cpf}}, responsável legal pelo(a) aluno(a) {{aluno.nome}}, matriculado(a) no(a) {{aluno.turma}}, autorizo {{escola.nome}}, CNPJ nº {{escola.cnpj}}, a captar e usar a imagem e a voz do(a) aluno(a) em fotos e vídeos de atividades pedagógicas, eventos e passeios da escola.</p>

        <h2>Onde a imagem pode aparecer</h2>
        <p>{{canais}}</p>

        <h2>Condições</h2>
        <ul>
          <li>O uso é gratuito e se limita a fins institucionais e pedagógicos, sem finalidade comercial direta.</li>
          <li>Não serão divulgados o nome completo, o endereço ou outros dados que permitam localizar o(a) aluno(a).</li>
          <li>Não serão publicadas imagens que exponham o(a) aluno(a) a situação vexatória ou constrangedora (art. 17 do Estatuto da Criança e do Adolescente).</li>
          <li>Esta autorização vale para o ano letivo de {{ano.letivo}} e pode ser revogada a qualquer momento, por escrito, valendo para as publicações posteriores ao pedido.</li>
          <li>Os dados são tratados no melhor interesse da criança e do adolescente, conforme o art. 14 da Lei nº 13.709/2018 (LGPD).</li>
        </ul>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("responsavel.nome", "Nome do responsável legal"),
            Doc("responsavel.cpf", "CPF do responsável"),
            Text("aluno.nome", "Nome do aluno"),
            Text("aluno.turma", "Turma do aluno (ex.: 3º ano B)"),
            Text("escola.nome", "Nome da escola"),
            Doc("escola.cnpj", "CNPJ da escola"),
            Text("canais", "Canais autorizados (ex.: site, redes sociais e agenda da escola)"),
            Text("ano.letivo", "Ano letivo (ex.: 2027)"),
            .. Closing(),
        ],
        [Signs("Responsável legal", 1)]);
}

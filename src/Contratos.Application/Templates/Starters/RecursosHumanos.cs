namespace Contratos.Application.Templates;

public static partial class TemplateStarters
{
    private static IEnumerable<TemplateStarter> RecursosHumanos() =>
    [
        ContratoDeExperiencia(),
        TermoDeEstagio(),
        AcordoDeTeletrabalho(),
        TermoDeEquipamentos(),
    ];

    private static TemplateStarter ContratoDeExperiencia() => new(
        "contrato-experiencia", "rh",
        "Contrato de trabalho por experiência",
        "Admissão CLT com período de experiência de até 90 dias, cargo, salário, jornada e prorrogação.",
        """
        <h1>Contrato de Trabalho por Prazo Determinado – Experiência</h1>
        <p><strong>EMPREGADORA:</strong> {{empresa.razao_social}}, inscrita no CNPJ sob o nº {{empresa.cnpj}}, com sede em {{empresa.endereco}}.</p>
        <p><strong>EMPREGADO(A):</strong> {{empregado.nome}}, inscrito(a) no CPF sob o nº {{empregado.cpf}}, residente em {{empregado.endereco}}.</p>
        <p>As partes celebram o presente contrato de experiência, nos termos dos arts. 443, § 2º, alínea “c”, e 445, parágrafo único, da Consolidação das Leis do Trabalho (CLT).</p>

        <h2>Cláusula 1ª – Da função</h2>
        <p>1.1. O(A) EMPREGADO(A) é admitido(a) para exercer a função de {{cargo}}{{#se cbo}} (CBO {{cbo}}){{/se}}, comprometendo-se a executar também as tarefas compatíveis com sua condição pessoal.</p>

        <h2>Cláusula 2ª – Do salário</h2>
        <p>2.1. O(A) EMPREGADO(A) receberá salário mensal de {{salario}}, pago até o 5º (quinto) dia útil do mês seguinte ao trabalhado, com os descontos previstos em lei.</p>

        <h2>Cláusula 3ª – Da jornada e do local</h2>
        <p>3.1. A jornada será de {{jornada}}, com intervalo para repouso e alimentação conforme a lei.</p>
        <p>3.2. O trabalho será prestado em {{local.trabalho}}, podendo o(a) EMPREGADO(A) ser transferido(a) nos limites do art. 469 da CLT.</p>

        <h2>Cláusula 4ª – Do prazo</h2>
        <p>4.1. Este contrato vigora por {{prazo.dias}} dias, de {{prazo.inicio}} em diante, e poderá ser prorrogado uma única vez, desde que a soma dos dois períodos não ultrapasse 90 (noventa) dias.</p>
        <p>4.2. Terminado o prazo sem manifestação das partes, o contrato passa a vigorar por prazo indeterminado.</p>

        <h2>Cláusula 5ª – Das obrigações</h2>
        <p>5.1. O(A) EMPREGADO(A) se obriga a cumprir o regulamento interno da empresa, as normas de saúde e segurança do trabalho e a manter sigilo sobre informações a que tiver acesso em razão da função.</p>
        <p>5.2. Os danos causados pelo(a) EMPREGADO(A) poderão ser descontados de seu salário em caso de dolo ou, havendo culpa, conforme autorizado neste contrato (art. 462, § 1º, da CLT).</p>

        <h2>Cláusula 6ª – Da proteção de dados</h2>
        <p>6.1. A EMPREGADORA tratará os dados pessoais do(a) EMPREGADO(A) para a execução deste contrato e o cumprimento de obrigações legais (eSocial, FGTS, INSS e outras), nos termos da Lei nº 13.709/2018.</p>

        {{#se prorrogacao.dias}}<h2>Cláusula 7ª – Da prorrogação</h2>
        <p>7.1. Por acordo das partes, este contrato fica prorrogado por mais {{prorrogacao.dias}} dias, respeitado o limite total de 90 (noventa) dias.</p>{{/se}}

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("empresa.razao_social", "Razão social da empresa"),
            Doc("empresa.cnpj", "CNPJ da empresa"),
            Text("empresa.endereco", "Endereço da empresa"),
            Text("empregado.nome", "Nome do empregado"),
            Doc("empregado.cpf", "CPF do empregado"),
            Text("empregado.endereco", "Endereço do empregado"),
            Text("cargo", "Cargo ou função"),
            Text("cbo", "Código CBO da função", required: false),
            Money("salario", "Salário mensal"),
            Text("jornada", "Jornada (ex.: 44 horas semanais, de segunda a sexta, das 8h às 17h48)"),
            Text("local.trabalho", "Local de trabalho"),
            Number("prazo.dias", "Prazo da experiência (dias)"),
            Date("prazo.inicio", "Data de admissão"),
            Number("prorrogacao.dias", "Dias de prorrogação já acertada", required: false),
            .. Closing(),
        ],
        [Signs("Empregadora", 1), Signs("Empregado", 2)]);

    private static TemplateStarter TermoDeEstagio() => new(
        "termo-estagio", "rh",
        "Termo de compromisso de estágio",
        "Estágio pela Lei nº 11.788/2008, entre estudante, empresa e instituição de ensino, com bolsa, jornada e seguro.",
        """
        <h1>Termo de Compromisso de Estágio</h1>
        <p><strong>CONCEDENTE:</strong> {{empresa.razao_social}}, inscrita no CNPJ sob o nº {{empresa.cnpj}}, com sede em {{empresa.endereco}}.</p>
        <p><strong>ESTAGIÁRIO(A):</strong> {{estagiario.nome}}, inscrito(a) no CPF sob o nº {{estagiario.cpf}}, aluno(a) do curso de {{estagiario.curso}}, matrícula nº {{estagiario.matricula}}.</p>
        <p><strong>INSTITUIÇÃO DE ENSINO:</strong> {{instituicao.nome}}, inscrita no CNPJ sob o nº {{instituicao.cnpj}}.</p>
        <p>As partes celebram o presente Termo de Compromisso de Estágio {{estagio.tipo}}, nos termos da Lei nº 11.788/2008, que não gera vínculo empregatício.</p>

        <h2>Cláusula 1ª – Das atividades</h2>
        <p>1.1. O(A) ESTAGIÁRIO(A) desenvolverá as atividades previstas no plano de estágio, compatíveis com o seu curso: {{estagio.atividades}}.</p>
        <p>1.2. O estágio será acompanhado pelo(a) supervisor(a) {{supervisor.nome}}, da CONCEDENTE, e pelo(a) professor(a) orientador(a) indicado(a) pela INSTITUIÇÃO DE ENSINO.</p>

        <h2>Cláusula 2ª – Da jornada e da vigência</h2>
        <p>2.1. A jornada será de {{estagio.jornada}}, compatível com as atividades escolares e limitada a 6 (seis) horas diárias e 30 (trinta) horas semanais.</p>
        <p>2.2. O estágio vigorará de {{vigencia.inicio}} a {{vigencia.fim}}, não podendo exceder 2 (dois) anos na mesma CONCEDENTE, salvo para pessoa com deficiência.</p>

        <h2>Cláusula 3ª – Da bolsa e dos benefícios</h2>
        <p>3.1. O(A) ESTAGIÁRIO(A) receberá bolsa mensal de {{bolsa.valor}} e auxílio-transporte de {{auxilio.transporte}}.</p>
        <p>3.2. O(A) ESTAGIÁRIO(A) terá recesso remunerado de 30 (trinta) dias a cada 12 (doze) meses de estágio, ou proporcional, preferencialmente nas férias escolares.</p>

        <h2>Cláusula 4ª – Do seguro</h2>
        <p>4.1. A CONCEDENTE contratou em favor do(a) ESTAGIÁRIO(A) seguro contra acidentes pessoais, apólice nº {{seguro.apolice}}, da seguradora {{seguro.seguradora}}.</p>

        <h2>Cláusula 5ª – Das obrigações</h2>
        <p>5.1. O(A) ESTAGIÁRIO(A) se obriga a cumprir a programação do estágio, as normas internas da CONCEDENTE, manter sigilo sobre as informações a que tiver acesso e entregar os relatórios exigidos pela INSTITUIÇÃO DE ENSINO.</p>
        <p>5.2. O estágio se encerra automaticamente com a conclusão ou o abandono do curso, ou com o trancamento da matrícula.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("empresa.razao_social", "Razão social da empresa concedente"),
            Doc("empresa.cnpj", "CNPJ da concedente"),
            Text("empresa.endereco", "Endereço da concedente"),
            Text("estagiario.nome", "Nome do estagiário"),
            Doc("estagiario.cpf", "CPF do estagiário"),
            Text("estagiario.curso", "Curso do estagiário"),
            Text("estagiario.matricula", "Matrícula do estagiário"),
            Text("instituicao.nome", "Instituição de ensino"),
            Doc("instituicao.cnpj", "CNPJ da instituição de ensino"),
            Text("estagio.tipo", "Tipo de estágio (obrigatório ou não obrigatório)"),
            Text("estagio.atividades", "Atividades do estágio"),
            Text("supervisor.nome", "Supervisor na empresa"),
            Text("estagio.jornada", "Jornada (ex.: 6 horas diárias, de segunda a sexta)"),
            Date("vigencia.inicio", "Início do estágio"),
            Date("vigencia.fim", "Fim do estágio"),
            Money("bolsa.valor", "Valor da bolsa"),
            Money("auxilio.transporte", "Auxílio-transporte mensal"),
            Text("seguro.apolice", "Número da apólice de seguro"),
            Text("seguro.seguradora", "Seguradora"),
            .. Closing(),
        ],
        [Signs("Concedente", 1), Signs("Estagiário", 2), Signs("Instituição de ensino", 3)]);

    private static TemplateStarter AcordoDeTeletrabalho() => new(
        "acordo-teletrabalho", "rh",
        "Aditivo de teletrabalho (home office)",
        "Muda o contrato para trabalho remoto ou híbrido, com equipamentos, reembolso de despesas e regras de jornada.",
        """
        <h1>Termo Aditivo ao Contrato de Trabalho – Teletrabalho</h1>
        <p><strong>EMPREGADORA:</strong> {{empresa.razao_social}}, inscrita no CNPJ sob o nº {{empresa.cnpj}}.</p>
        <p><strong>EMPREGADO(A):</strong> {{empregado.nome}}, inscrito(a) no CPF sob o nº {{empregado.cpf}}, ocupante do cargo de {{empregado.cargo}}.</p>
        <p>As partes, de comum acordo e nos termos dos arts. 75-A a 75-F da CLT, aditam o contrato de trabalho vigente, nas condições abaixo.</p>

        <h2>Cláusula 1ª – Do regime</h2>
        <p>1.1. A partir de {{inicio}}, o(a) EMPREGADO(A) prestará serviços em regime de {{regime}}, a partir de {{local.remoto}}.</p>
        <p>1.2. O comparecimento às dependências da EMPREGADORA para atividades específicas não descaracteriza o teletrabalho.</p>

        <h2>Cláusula 2ª – Da jornada</h2>
        <p>2.1. {{jornada.regras}}</p>
        <p>2.2. Fora do horário de trabalho, o(a) EMPREGADO(A) não é obrigado(a) a responder mensagens ou chamadas, ressalvados os regimes de sobreaviso previamente acordados.</p>

        <h2>Cláusula 3ª – Dos equipamentos e das despesas</h2>
        <p>3.1. A EMPREGADORA fornecerá os seguintes equipamentos, em comodato: {{equipamentos}}.</p>
        <p>3.2. A título de ajuda de custo com internet e energia, a EMPREGADORA pagará {{ajuda.valor}} por mês, valor que não integra a remuneração.</p>

        <h2>Cláusula 4ª – Da saúde e da segurança</h2>
        <p>4.1. O(A) EMPREGADO(A) declara ter recebido orientações sobre ergonomia e prevenção de doenças e acidentes de trabalho, comprometendo-se a segui-las.</p>

        <h2>Cláusula 5ª – Da segurança da informação</h2>
        <p>5.1. O(A) EMPREGADO(A) usará os equipamentos e acessos apenas para o trabalho, seguirá a política de segurança da informação da EMPREGADORA e não permitirá o acesso de terceiros às informações da empresa.</p>

        <h2>Cláusula 6ª – Da reversão</h2>
        <p>6.1. A EMPREGADORA poderá determinar o retorno ao regime presencial, com prazo de transição mínimo de 15 (quinze) dias, registrado em aditivo.</p>

        <p>Ficam mantidas as demais cláusulas do contrato de trabalho.</p>
        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("empresa.razao_social", "Razão social da empresa"),
            Doc("empresa.cnpj", "CNPJ da empresa"),
            Text("empregado.nome", "Nome do empregado"),
            Doc("empregado.cpf", "CPF do empregado"),
            Text("empregado.cargo", "Cargo do empregado"),
            Date("inicio", "Início do teletrabalho"),
            Text("regime", "Regime (ex.: teletrabalho integral, híbrido com 2 dias presenciais)"),
            Text("local.remoto", "Local de onde vai trabalhar (ex.: residência do empregado)"),
            Text("jornada.regras", "Regras de jornada (ex.: jornada de 8h com controle por sistema de ponto)"),
            Text("equipamentos", "Equipamentos fornecidos"),
            Money("ajuda.valor", "Ajuda de custo mensal"),
            .. Closing(),
        ],
        [Signs("Empregadora", 1), Signs("Empregado", 2)]);

    private static TemplateStarter TermoDeEquipamentos() => new(
        "termo-equipamentos", "rh",
        "Termo de responsabilidade por equipamentos",
        "Entrega de notebook, celular ou outro bem ao colaborador, com cuidados, uso e devolução.",
        """
        <h1>Termo de Responsabilidade pela Guarda e Uso de Equipamentos</h1>
        <p>Eu, {{colaborador.nome}}, inscrito(a) no CPF sob o nº {{colaborador.cpf}}, {{colaborador.cargo}} de {{empresa.razao_social}}, CNPJ nº {{empresa.cnpj}}, declaro ter recebido, em perfeito estado de conservação e funcionamento, os seguintes equipamentos:</p>
        <p>{{equipamentos.lista}}</p>
        <p>Valor total estimado dos equipamentos: {{equipamentos.valor}}.</p>

        <h2>Compromissos</h2>
        <ol>
          <li>Usar os equipamentos exclusivamente para as atividades de trabalho, seguindo a política de uso e de segurança da informação da empresa.</li>
          <li>Zelar pela guarda e conservação dos equipamentos e não os emprestar, ceder ou alterar sem autorização.</li>
          <li>Comunicar imediatamente à empresa qualquer defeito, perda, furto ou roubo, apresentando boletim de ocorrência quando for o caso.</li>
          <li>Devolver os equipamentos, com seus acessórios, quando solicitado ou no desligamento, no mesmo estado em que os recebi, ressalvado o desgaste natural pelo uso.</li>
          <li>Ressarcir a empresa pelos danos causados por dolo ou por culpa, autorizando o desconto em folha nos termos do art. 462, § 1º, da CLT.</li>
        </ol>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("colaborador.nome", "Nome do colaborador"),
            Doc("colaborador.cpf", "CPF do colaborador"),
            Text("colaborador.cargo", "Cargo do colaborador"),
            Text("empresa.razao_social", "Razão social da empresa"),
            Doc("empresa.cnpj", "CNPJ da empresa"),
            Text("equipamentos.lista", "Equipamentos entregues (modelo e nº de série)"),
            Money("equipamentos.valor", "Valor total estimado"),
            .. Closing(),
        ],
        [Signs("Colaborador", 1), Signs("Empresa", 2)]);
}

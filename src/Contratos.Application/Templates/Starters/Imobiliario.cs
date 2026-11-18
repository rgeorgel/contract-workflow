namespace Contratos.Application.Templates;

public static partial class TemplateStarters
{
    private static IEnumerable<TemplateStarter> Imobiliario() =>
    [
        LocacaoResidencial(),
        LocacaoComercial(),
        AdministracaoDeImovel(),
        IntermediacaoDeVenda(),
    ];

    private static TemplateStarter LocacaoResidencial() => new(
        "locacao-residencial", "imobiliario",
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
            .. Forum(),
        ],
        [Signs("Locador", 1), Signs("Locatário", 2)]);

    private static TemplateStarter LocacaoComercial() => new(
        "locacao-comercial", "imobiliario",
        "Locação comercial",
        "Aluguel de sala, loja ou galpão para uma empresa, com atividade, carência, reajuste, garantia e renovação.",
        """
        <h1>Contrato de Locação Não Residencial</h1>
        <p><strong>LOCADOR(A):</strong> {{locador.nome}}, inscrito(a) no CPF/CNPJ sob o nº {{locador.documento}}, com endereço em {{locador.endereco}}.</p>
        <p><strong>LOCATÁRIA:</strong> {{locataria.razao_social}}, inscrita no CNPJ sob o nº {{locataria.cnpj}}, com sede em {{locataria.endereco}}, neste ato representada por {{locataria.representante}}.</p>
        <p>As partes celebram o presente contrato de locação para fins não residenciais, regido pela Lei nº 8.245/1991 e pelas cláusulas seguintes.</p>

        <h2>Cláusula 1ª – Do imóvel e da destinação</h2>
        <p>1.1. É objeto desta locação o imóvel situado em {{imovel.endereco}}{{#se imovel.matricula}}, matrícula nº {{imovel.matricula}}{{/se}}.</p>
        <p>1.2. O imóvel destina-se exclusivamente à seguinte atividade: {{imovel.atividade}}. Cabe à LOCATÁRIA obter alvarás, licenças e o AVCB necessários ao seu funcionamento.</p>

        <h2>Cláusula 2ª – Do prazo</h2>
        <p>2.1. A locação vigorará por {{prazo.meses}} meses, de {{prazo.inicio}} em diante. A LOCATÁRIA poderá exercer o direito à renovação nas condições do art. 51 da Lei nº 8.245/1991.</p>

        <h2>Cláusula 3ª – Do aluguel</h2>
        <p>3.1. O aluguel mensal é de {{aluguel.valor}}, com vencimento no dia {{aluguel.vencimento}} de cada mês{{#se aluguel.carencia_meses}}, com carência de {{aluguel.carencia_meses}} mês(es) contada do início, destinada à adaptação do imóvel{{/se}}.</p>
        <p>3.2. O aluguel será reajustado anualmente pelo {{reajuste.indice}}. O atraso sujeitará a LOCATÁRIA a multa de 10% (dez por cento), juros de 1% (um por cento) ao mês e correção monetária.</p>

        <h2>Cláusula 4ª – Dos encargos</h2>
        <p>4.1. São de responsabilidade da LOCATÁRIA o IPTU, as despesas ordinárias de condomínio, o seguro contra incêndio e as contas de consumo do imóvel.</p>

        <h2>Cláusula 5ª – Das obras e benfeitorias</h2>
        <p>5.1. Adaptações e benfeitorias dependem de autorização escrita do(a) LOCADOR(A) e não serão indenizadas, podendo ser retiradas pela LOCATÁRIA ao final, desde que o imóvel seja devolvido no estado em que foi recebido.</p>

        <h2>Cláusula 6ª – Da garantia</h2>
        <p>6.1. Em garantia deste contrato, a LOCATÁRIA oferece: {{garantia.descricao}}.</p>

        <h2>Cláusula 7ª – Da cessão e da sublocação</h2>
        <p>7.1. É vedada a cessão, a sublocação ou o empréstimo do imóvel, no todo ou em parte, sem o consentimento prévio e por escrito do(a) LOCADOR(A).</p>

        <h2>Cláusula 8ª – Da rescisão</h2>
        <p>8.1. A devolução antecipada do imóvel sujeitará a LOCATÁRIA a multa equivalente a {{multa.alugueis}} aluguéis, proporcional ao período restante do contrato.</p>

        <h2>Cláusula 9ª – Do foro</h2>
        <p>Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer dúvidas oriundas deste contrato.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("locador.nome", "Nome do locador"),
            Doc("locador.documento", "CPF/CNPJ do locador"),
            Text("locador.endereco", "Endereço do locador"),
            Text("locataria.razao_social", "Razão social da empresa locatária"),
            Doc("locataria.cnpj", "CNPJ da locatária"),
            Text("locataria.endereco", "Endereço da sede da locatária"),
            Text("locataria.representante", "Representante legal da locatária"),
            Text("imovel.endereco", "Endereço do imóvel"),
            Text("imovel.matricula", "Matrícula do imóvel", required: false),
            Text("imovel.atividade", "Atividade que será exercida no imóvel"),
            Number("prazo.meses", "Prazo em meses"),
            Date("prazo.inicio", "Início da locação"),
            Money("aluguel.valor", "Valor do aluguel"),
            Number("aluguel.vencimento", "Dia do vencimento"),
            Number("aluguel.carencia_meses", "Meses de carência", required: false),
            Text("reajuste.indice", "Índice de reajuste (ex.: IPCA)"),
            Text("garantia.descricao", "Garantia (ex.: fiança bancária, seguro-fiança)"),
            Number("multa.alugueis", "Multa por saída antecipada (nº de aluguéis)"),
            .. Forum(),
        ],
        [Signs("Locador", 1), Signs("Locatária", 2), Witness(3), Witness(4)]);

    private static TemplateStarter AdministracaoDeImovel() => new(
        "administracao-imovel", "imobiliario",
        "Administração de imóvel para locação",
        "O proprietário entrega o imóvel à imobiliária para anunciar, alugar, cobrar e repassar os aluguéis.",
        """
        <h1>Contrato de Administração de Imóvel</h1>
        <p><strong>PROPRIETÁRIO(A):</strong> {{proprietario.nome}}, inscrito(a) no CPF/CNPJ sob o nº {{proprietario.documento}}, com endereço em {{proprietario.endereco}}.</p>
        <p><strong>ADMINISTRADORA:</strong> {{imobiliaria.nome}}, inscrita no CNPJ sob o nº {{imobiliaria.cnpj}} e no CRECI sob o nº {{imobiliaria.creci}}, com sede em {{imobiliaria.endereco}}.</p>

        <h2>Cláusula 1ª – Do objeto</h2>
        <p>1.1. O(A) PROPRIETÁRIO(A) entrega à ADMINISTRADORA a administração do imóvel situado em {{imovel.endereco}}, para fins de locação, pelo aluguel mínimo de {{aluguel.minimo}}, que poderá ser alterado com sua concordância.</p>

        <h2>Cláusula 2ª – Das atribuições da ADMINISTRADORA</h2>
        <p>2.1. Compete à ADMINISTRADORA, em nome do(a) PROPRIETÁRIO(A): anunciar o imóvel, selecionar e aprovar o cadastro de interessados, assinar contratos de locação e seus aditivos, realizar as vistorias de entrada e de saída, emitir boletos, cobrar e receber aluguéis e encargos, e promover a cobrança, inclusive judicial, por meio de advogados por ela indicados.</p>
        <p>2.2. Reparos acima de {{reparos.limite}} dependem de autorização prévia do(a) PROPRIETÁRIO(A), salvo em situações de urgência.</p>

        <h2>Cláusula 3ª – Da remuneração</h2>
        <p>3.1. Pela intermediação da primeira locação, a ADMINISTRADORA receberá {{taxa.intermediacao}}.</p>
        <p>3.2. Pela administração, a ADMINISTRADORA receberá {{taxa.administracao_percentual}}% sobre cada aluguel efetivamente recebido, descontados no momento do repasse.</p>

        <h2>Cláusula 4ª – Dos repasses e da prestação de contas</h2>
        <p>4.1. Os valores recebidos serão repassados ao(à) PROPRIETÁRIO(A) até o dia {{repasse.dia}} de cada mês, por transferência para a conta: {{repasse.conta}}, acompanhados de demonstrativo mensal.</p>
        {{#se garantia.aluguel}}<p>4.2. A ADMINISTRADORA garante o repasse do aluguel mesmo em caso de inadimplência do locatário, nas condições a seguir: {{garantia.aluguel}}.</p>{{/se}}

        <h2>Cláusula 5ª – Das obrigações do(a) PROPRIETÁRIO(A)</h2>
        <p>5.1. Manter a documentação do imóvel regular, responder pelas despesas extraordinárias de condomínio e pelos reparos estruturais, e informar à ADMINISTRADORA qualquer alteração de seus dados bancários e de contato.</p>

        <h2>Cláusula 6ª – Da vigência e da rescisão</h2>
        <p>6.1. Este contrato vigora por prazo indeterminado e pode ser rescindido por qualquer das partes mediante aviso por escrito com {{aviso.dias}} dias de antecedência. Havendo locação em curso, a ADMINISTRADORA entregará ao(à) PROPRIETÁRIO(A) o contrato de locação, as garantias e a documentação do locatário.</p>

        <h2>Cláusula 7ª – Do foro</h2>
        <p>Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer dúvidas oriundas deste contrato.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("proprietario.nome", "Nome do proprietário"),
            Doc("proprietario.documento", "CPF/CNPJ do proprietário"),
            Text("proprietario.endereco", "Endereço do proprietário"),
            Text("imobiliaria.nome", "Nome da imobiliária"),
            Doc("imobiliaria.cnpj", "CNPJ da imobiliária"),
            Text("imobiliaria.creci", "CRECI da imobiliária"),
            Text("imobiliaria.endereco", "Endereço da imobiliária"),
            Text("imovel.endereco", "Endereço do imóvel administrado"),
            Money("aluguel.minimo", "Aluguel mínimo pretendido"),
            Money("reparos.limite", "Limite para reparos sem autorização"),
            Text("taxa.intermediacao", "Taxa de intermediação (ex.: o primeiro aluguel integral)"),
            Number("taxa.administracao_percentual", "Taxa de administração (%)"),
            Number("repasse.dia", "Dia do repasse"),
            Text("repasse.conta", "Conta para repasse (banco, agência, conta ou Pix)"),
            Text("garantia.aluguel", "Condições do aluguel garantido", required: false),
            Number("aviso.dias", "Aviso prévio para rescindir (dias)"),
            .. Forum(),
        ],
        [Signs("Proprietário", 1), Signs("Administradora", 2)]);

    private static TemplateStarter IntermediacaoDeVenda() => new(
        "intermediacao-venda", "imobiliario",
        "Autorização de venda com exclusividade",
        "O proprietário autoriza o corretor a vender o imóvel por um prazo, com preço, comissão e exclusividade.",
        """
        <h1>Contrato de Intermediação Imobiliária com Exclusividade</h1>
        <p><strong>PROPRIETÁRIO(A):</strong> {{proprietario.nome}}, inscrito(a) no CPF/CNPJ sob o nº {{proprietario.documento}}, com endereço em {{proprietario.endereco}}.</p>
        <p><strong>CORRETOR(A):</strong> {{corretor.nome}}, inscrito(a) no CPF/CNPJ sob o nº {{corretor.documento}} e no CRECI sob o nº {{corretor.creci}}, com endereço em {{corretor.endereco}}.</p>

        <h2>Cláusula 1ª – Do imóvel e do preço</h2>
        <p>1.1. O(A) PROPRIETÁRIO(A) autoriza o(a) CORRETOR(A) a promover a venda do imóvel situado em {{imovel.endereco}}{{#se imovel.matricula}}, matrícula nº {{imovel.matricula}}{{/se}}, pelo preço de {{imovel.preco}}, nas seguintes condições: {{venda.condicoes}}.</p>

        <h2>Cláusula 2ª – Da exclusividade e do prazo</h2>
        <p>2.1. Esta autorização é dada com exclusividade, pelo prazo de {{prazo.dias}} dias contados da assinatura, nos termos do art. 726 do Código Civil.</p>
        <p>2.2. Durante o prazo, a comissão será devida ainda que a venda seja feita diretamente pelo(a) PROPRIETÁRIO(A) ou por outro intermediário.</p>
        <p>2.3. Também será devida a comissão se, em até {{prazo.posterior_dias}} dias após o fim do prazo, o imóvel for vendido a comprador apresentado pelo(a) CORRETOR(A) durante a vigência.</p>

        <h2>Cláusula 3ª – Da comissão</h2>
        <p>3.1. A comissão é de {{comissao.percentual}}% sobre o valor efetivo da venda, paga no ato do recebimento do sinal ou da assinatura do compromisso de compra e venda, o que ocorrer primeiro.</p>

        <h2>Cláusula 4ª – Das obrigações</h2>
        <p>4.1. O(A) CORRETOR(A) se obriga a divulgar o imóvel, acompanhar visitas, prestar informações sobre o andamento do negócio e apoiar a reunião da documentação.</p>
        <p>4.2. O(A) PROPRIETÁRIO(A) declara que o imóvel está livre de ônus que impeçam a venda e autoriza a divulgação de fotos e a colocação de placa no local.</p>

        <h2>Cláusula 5ª – Do foro</h2>
        <p>Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer dúvidas oriundas deste contrato.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("proprietario.nome", "Nome do proprietário"),
            Doc("proprietario.documento", "CPF/CNPJ do proprietário"),
            Text("proprietario.endereco", "Endereço do proprietário"),
            Text("corretor.nome", "Nome do corretor ou imobiliária"),
            Doc("corretor.documento", "CPF/CNPJ do corretor"),
            Text("corretor.creci", "CRECI do corretor"),
            Text("corretor.endereco", "Endereço do corretor"),
            Text("imovel.endereco", "Endereço do imóvel à venda"),
            Text("imovel.matricula", "Matrícula do imóvel", required: false),
            Money("imovel.preco", "Preço de venda"),
            Text("venda.condicoes", "Condições de pagamento aceitas (ex.: à vista ou financiamento)"),
            Number("prazo.dias", "Prazo da exclusividade (dias)"),
            Number("prazo.posterior_dias", "Dias após o prazo em que a comissão ainda vale"),
            Number("comissao.percentual", "Comissão (%)"),
            .. Forum(),
        ],
        [Signs("Proprietário", 1), Signs("Corretor", 2)]);
}

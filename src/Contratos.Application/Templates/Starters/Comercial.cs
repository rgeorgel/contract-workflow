namespace Contratos.Application.Templates;

public static partial class TemplateStarters
{
    private static IEnumerable<TemplateStarter> Comercial() =>
    [
        RepresentacaoComercial(),
        CompraEVendaDeBemMovel(),
        Fornecimento(),
    ];

    private static TemplateStarter RepresentacaoComercial() => new(
        "representacao-comercial", "comercial",
        "Representação comercial",
        "Representante autônomo que vende os produtos da empresa numa região, com comissão e regras da Lei nº 4.886/1965.",
        """
        <h1>Contrato de Representação Comercial Autônoma</h1>
        <p><strong>REPRESENTADA:</strong> {{representada.razao_social}}, inscrita no CNPJ sob o nº {{representada.cnpj}}, com sede em {{representada.endereco}}.</p>
        <p><strong>REPRESENTANTE:</strong> {{representante.nome}}, inscrito(a) no CPF/CNPJ sob o nº {{representante.documento}} e no Core sob o nº {{representante.core}}, com endereço em {{representante.endereco}}.</p>
        <p>As partes celebram o presente contrato, regido pela Lei nº 4.886/1965, nas condições abaixo.</p>

        <h2>Cláusula 1ª – Do objeto e da zona</h2>
        <p>1.1. O(A) REPRESENTANTE promoverá, em caráter não eventual e sem vínculo empregatício, a venda dos seguintes produtos da REPRESENTADA: {{produtos}}.</p>
        <p>1.2. A representação abrange a seguinte zona: {{zona}}, {{exclusividade}}.</p>

        <h2>Cláusula 2ª – Dos pedidos</h2>
        <p>2.1. Os pedidos serão encaminhados à REPRESENTADA, que poderá recusá-los por escrito em até 15 (quinze) dias. Sem manifestação nesse prazo, consideram-se aceitos.</p>
        <p>2.2. Os preços, prazos e condições de pagamento seguirão a tabela vigente da REPRESENTADA.</p>

        <h2>Cláusula 3ª – Da comissão</h2>
        <p>3.1. O(A) REPRESENTANTE receberá comissão de {{comissao.percentual}}% sobre o valor total das mercadorias de cada pedido, paga até o dia {{comissao.dia_pagamento}} do mês seguinte ao do pagamento pelo cliente.</p>
        <p>3.2. A comissão será devida também sobre os pedidos diretos da REPRESENTADA na zona do(a) REPRESENTANTE, quando houver exclusividade.</p>

        <h2>Cláusula 4ª – Das obrigações do(a) REPRESENTANTE</h2>
        <p>4.1. Prestar informações sobre o mercado e a solvência dos clientes, seguir as instruções da REPRESENTADA, manter sigilo sobre suas informações comerciais e manter regular o registro no Conselho Regional dos Representantes Comerciais.</p>

        <h2>Cláusula 5ª – Da vigência e da rescisão</h2>
        <p>5.1. Este contrato vigora por prazo indeterminado a partir de {{contrato.inicio}}.</p>
        <p>5.2. Na rescisão sem justo motivo pela REPRESENTADA, serão devidos o aviso prévio de 30 (trinta) dias ou o pagamento de 1/3 (um terço) das comissões dos últimos três meses, e a indenização de 1/12 (um doze avos) do total das comissões recebidas durante a representação (arts. 27, “j”, e 34 da Lei nº 4.886/1965).</p>

        <h2>Cláusula 6ª – Do foro</h2>
        <p>Fica eleito o foro do domicílio do(a) REPRESENTANTE, em {{foro.cidade}}, conforme o art. 39 da Lei nº 4.886/1965.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("representada.razao_social", "Razão social da representada"),
            Doc("representada.cnpj", "CNPJ da representada"),
            Text("representada.endereco", "Endereço da representada"),
            Text("representante.nome", "Nome do representante"),
            Doc("representante.documento", "CPF/CNPJ do representante"),
            Text("representante.core", "Registro no Core"),
            Text("representante.endereco", "Endereço do representante"),
            Text("produtos", "Produtos representados"),
            Text("zona", "Zona de atuação (cidades ou estados)"),
            Text("exclusividade", "Exclusividade (ex.: com exclusividade / sem exclusividade)"),
            Number("comissao.percentual", "Comissão (%)"),
            Number("comissao.dia_pagamento", "Dia de pagamento da comissão"),
            Date("contrato.inicio", "Início da representação"),
            .. Forum(),
        ],
        [Signs("Representada", 1), Signs("Representante", 2)]);

    private static TemplateStarter CompraEVendaDeBemMovel() => new(
        "compra-venda-bem-movel", "comercial",
        "Compra e venda de bem móvel",
        "Venda de veículo, máquina ou equipamento, com preço, pagamento, entrega e declaração do estado do bem.",
        """
        <h1>Contrato de Compra e Venda de Bem Móvel</h1>
        <p><strong>VENDEDOR(A):</strong> {{vendedor.nome}}, inscrito(a) no CPF/CNPJ sob o nº {{vendedor.documento}}, com endereço em {{vendedor.endereco}}.</p>
        <p><strong>COMPRADOR(A):</strong> {{comprador.nome}}, inscrito(a) no CPF/CNPJ sob o nº {{comprador.documento}}, com endereço em {{comprador.endereco}}.</p>

        <h2>Cláusula 1ª – Do bem</h2>
        <p>1.1. O(A) VENDEDOR(A), legítimo(a) proprietário(a), vende ao(à) COMPRADOR(A) o seguinte bem: {{bem.descricao}}.</p>
        <p>1.2. O(A) VENDEDOR(A) declara que o bem está livre de ônus, dívidas, multas e restrições, respondendo pela evicção na forma da lei.</p>

        <h2>Cláusula 2ª – Do preço</h2>
        <p>2.1. O preço total é de {{preco.valor}}, pago da seguinte forma: {{pagamento.forma}}.</p>
        <p>2.2. A propriedade só se transfere com a quitação integral do preço.</p>

        <h2>Cláusula 3ª – Da entrega</h2>
        <p>3.1. O bem será entregue em {{entrega.data}}, em {{entrega.local}}, com seus acessórios e documentos. A partir da entrega, os riscos e as despesas com o bem passam ao(à) COMPRADOR(A).</p>

        <h2>Cláusula 4ª – Do estado do bem</h2>
        <p>4.1. O(A) COMPRADOR(A) declara ter examinado o bem e o aceita no estado em que se encontra: {{bem.estado}}. O(A) VENDEDOR(A) responde por vícios ocultos nos termos dos arts. 441 a 446 do Código Civil.</p>

        <h2>Cláusula 5ª – Da transferência</h2>
        <p>5.1. Quando o bem exigir registro (como veículos), as partes se obrigam a assinar a transferência e comunicá-la ao órgão competente em até 30 (trinta) dias da quitação, correndo as despesas por conta do(a) COMPRADOR(A).</p>

        <h2>Cláusula 6ª – Da multa</h2>
        <p>6.1. A parte que descumprir este contrato pagará à outra multa de {{multa.percentual}}% sobre o preço, sem prejuízo das perdas e danos.</p>

        <h2>Cláusula 7ª – Do foro</h2>
        <p>Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer dúvidas oriundas deste contrato.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("vendedor.nome", "Nome do vendedor"),
            Doc("vendedor.documento", "CPF/CNPJ do vendedor"),
            Text("vendedor.endereco", "Endereço do vendedor"),
            Text("comprador.nome", "Nome do comprador"),
            Doc("comprador.documento", "CPF/CNPJ do comprador"),
            Text("comprador.endereco", "Endereço do comprador"),
            Text("bem.descricao", "Descrição do bem (marca, modelo, ano, nº de série ou placa)"),
            Money("preco.valor", "Preço total"),
            Text("pagamento.forma", "Forma de pagamento"),
            Date("entrega.data", "Data de entrega"),
            Text("entrega.local", "Local de entrega"),
            Text("bem.estado", "Estado do bem (ex.: usado, em bom funcionamento)"),
            Number("multa.percentual", "Multa por descumprimento (%)"),
            .. Forum(),
        ],
        [Signs("Vendedor", 1), Signs("Comprador", 2)]);

    private static TemplateStarter Fornecimento() => new(
        "fornecimento", "comercial",
        "Fornecimento recorrente de produtos",
        "Acordo com fornecedor para entregas periódicas, com preço, pedidos, prazo de entrega, qualidade e reajuste.",
        """
        <h1>Contrato de Fornecimento</h1>
        <p><strong>FORNECEDORA:</strong> {{fornecedor.razao_social}}, inscrita no CNPJ sob o nº {{fornecedor.cnpj}}, com sede em {{fornecedor.endereco}}.</p>
        <p><strong>COMPRADORA:</strong> {{comprador.razao_social}}, inscrita no CNPJ sob o nº {{comprador.cnpj}}, com sede em {{comprador.endereco}}.</p>

        <h2>Cláusula 1ª – Do objeto</h2>
        <p>1.1. A FORNECEDORA fornecerá à COMPRADORA, de forma contínua, os seguintes produtos: {{produtos}}, de acordo com as especificações acordadas.</p>
        {{#se volume.minimo}}<p>1.2. A COMPRADORA se compromete a adquirir o volume mínimo de {{volume.minimo}} por mês.</p>{{/se}}

        <h2>Cláusula 2ª – Dos pedidos e da entrega</h2>
        <p>2.1. Os pedidos serão feitos por escrito, inclusive por e-mail ou sistema, e entregues em até {{entrega.prazo_dias}} dias, no endereço {{entrega.local}}.</p>
        <p>2.2. O frete corre por conta de {{frete.responsavel}}.</p>

        <h2>Cláusula 3ª – Do preço e do pagamento</h2>
        <p>3.1. Os preços são os da tabela anexa, pagos em {{pagamento.prazo_dias}} dias após a entrega, mediante nota fiscal e boleto.</p>
        <p>3.2. Os preços poderão ser reajustados a cada 12 (doze) meses pelo {{reajuste.indice}}, ou antes, por acordo escrito, em caso de variação extraordinária de custos.</p>

        <h2>Cláusula 4ª – Da qualidade</h2>
        <p>4.1. A COMPRADORA poderá recusar, em até 5 (cinco) dias úteis do recebimento, produtos fora da especificação ou avariados, que serão substituídos pela FORNECEDORA sem custo.</p>

        <h2>Cláusula 5ª – Do atraso</h2>
        <p>5.1. O atraso na entrega sujeitará a FORNECEDORA a multa de 0,5% (meio por cento) ao dia sobre o valor do pedido, limitada a 10% (dez por cento). O atraso no pagamento sujeitará a COMPRADORA a multa de 2% (dois por cento) e juros de 1% (um por cento) ao mês.</p>

        <h2>Cláusula 6ª – Da vigência</h2>
        <p>6.1. Este contrato vigora por {{vigencia.meses}} meses a partir de {{contrato.inicio}}, renovando-se automaticamente por iguais períodos, salvo aviso contrário com 30 (trinta) dias de antecedência.</p>

        <h2>Cláusula 7ª – Do foro</h2>
        <p>Fica eleito o foro da comarca de {{foro.cidade}} para dirimir quaisquer dúvidas oriundas deste contrato.</p>

        <p>{{assinatura.cidade}}, {{assinatura.data}}.</p>
        """,
        [
            Text("fornecedor.razao_social", "Razão social da fornecedora"),
            Doc("fornecedor.cnpj", "CNPJ da fornecedora"),
            Text("fornecedor.endereco", "Endereço da fornecedora"),
            Text("comprador.razao_social", "Razão social da compradora"),
            Doc("comprador.cnpj", "CNPJ da compradora"),
            Text("comprador.endereco", "Endereço da compradora"),
            Text("produtos", "Produtos fornecidos"),
            Text("volume.minimo", "Volume mínimo mensal (ex.: 200 caixas)", required: false),
            Number("entrega.prazo_dias", "Prazo de entrega (dias)"),
            Text("entrega.local", "Endereço de entrega"),
            Text("frete.responsavel", "Quem paga o frete (ex.: da fornecedora)"),
            Number("pagamento.prazo_dias", "Prazo de pagamento (dias após a entrega)"),
            Text("reajuste.indice", "Índice de reajuste (ex.: IPCA)"),
            Number("vigencia.meses", "Vigência (meses)"),
            Date("contrato.inicio", "Início do fornecimento"),
            .. Forum(),
        ],
        [Signs("Fornecedora", 1), Signs("Compradora", 2)]);
}

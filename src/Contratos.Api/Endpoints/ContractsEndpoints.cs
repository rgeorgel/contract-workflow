using Contratos.Application.Contracts;
using Contratos.Domain.Contracts;

namespace Contratos.Api.Endpoints;

internal static class ContractsEndpoints
{
    public static RouteGroupBuilder MapContractsEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/contracts").WithTags("Contratos").RequireAuthorization();

        g.MapGet("/", async (string? q, ContractStatus? status, Guid? templateId, DateOnly? from, DateOnly? to,
                int? page, int? pageSize, string? sort, string? dateField, Guid? createdBy, IContractService contracts, CancellationToken ct) =>
                TypedResults.Ok(await contracts.ListAsync(
                    new ContractListQuery(q, status, templateId, from, to, page ?? 1, pageSize ?? 20, sort, dateField, createdBy), ct)))
            .WithSummary("Busca e listagem paginada");

        g.MapPost("/", async (CreateContractRequest req, IContractService contracts, HttpContext http, CancellationToken ct) =>
                (await contracts.CreateAsync(req, ct)).ToHttp(c => WithETag(http, c, TypedResults.Created($"/api/v1/contracts/{c.Id}", c))))
            .WithSummary("Cria um rascunho a partir da versão publicada de um template");

        g.MapPost("/bulk", async (BulkContractsRequest req, IContractService contracts, CancellationToken ct) =>
                (await contracts.CreateBulkAsync(req, ct)).ToHttp())
            .WithSummary("Valida (dryRun) ou cria rascunhos em lote a partir das linhas de uma planilha");

        g.MapGet("/{id:guid}", async (Guid id, IContractService contracts, HttpContext http, CancellationToken ct) =>
                (await contracts.GetAsync(id, ct)).ToHttp(c => WithETag(http, c, TypedResults.Ok(c))))
            .WithSummary("Detalhe com signatários");

        g.MapPut("/{id:guid}", async (Guid id, UpdateContractRequest req, IContractService contracts, HttpContext http, CancellationToken ct) =>
                (await contracts.UpdateAsync(id, req, ETags.IfMatch(http), ct)).ToHttp(c => WithETag(http, c, TypedResults.Ok(c))))
            .WithSummary("Edita o rascunho (título, variáveis, expiração, modo, mensagem)");

        g.MapDelete("/{id:guid}", async (Guid id, int? confirmNumber, bool? alsoInAutentique, IContractService contracts, CancellationToken ct) =>
                (await contracts.DeleteAsync(id, confirmNumber ?? -1, alsoInAutentique ?? false, ct)).ToHttp())
            .WithSummary("Exclui o contrato (Admin ou criador); confirmNumber = número do contrato");

        g.MapPost("/{id:guid}/signers", async (Guid id, SignerRequest req, IContractService contracts, CancellationToken ct) =>
                (await contracts.AddSignerAsync(id, req, ct)).ToHttp())
            .WithSummary("Adiciona signatário (só em rascunho)");
        g.MapPut("/{id:guid}/signers/{signerId:guid}", async (Guid id, Guid signerId, SignerRequest req, IContractService contracts, CancellationToken ct) =>
                (await contracts.UpdateSignerAsync(id, signerId, req, ct)).ToHttp())
            .WithSummary("Edita signatário (só em rascunho)");
        g.MapDelete("/{id:guid}/signers/{signerId:guid}", async (Guid id, Guid signerId, IContractService contracts, CancellationToken ct) =>
                (await contracts.RemoveSignerAsync(id, signerId, ct)).ToHttp())
            .WithSummary("Remove signatário (só em rascunho)");
        g.MapPost("/{id:guid}/signers/{signerId:guid}/resend", async (Guid id, Guid signerId, IContractService contracts, CancellationToken ct) =>
                (await contracts.ResendAsync(id, signerId, ct)).ToHttp())
            .WithSummary("Reenvia o link pelo mesmo canal");

        g.MapPost("/{id:guid}/send", async (Guid id, HttpContext http, IContractService contracts, CancellationToken ct) =>
            {
                var key = http.Request.Headers["Idempotency-Key"].ToString();
                return (await contracts.SendAsync(id, string.IsNullOrWhiteSpace(key) ? null : key[..Math.Min(key.Length, 100)], ct))
                    .ToHttp(c => TypedResults.Accepted($"/api/v1/contracts/{c.Id}", c));
            })
            .WithSummary("Gera o PDF, congela e envia ao provedor (aceita Idempotency-Key)");
        g.MapPost("/{id:guid}/retry-send", async (Guid id, IContractService contracts, CancellationToken ct) =>
                (await contracts.RetrySendAsync(id, ct)).ToHttp())
            .WithSummary("Repete um envio que falhou");
        g.MapPost("/{id:guid}/return-to-draft", async (Guid id, IContractService contracts, CancellationToken ct) =>
                (await contracts.ReturnToDraftAsync(id, ct)).ToHttp())
            .WithSummary("Envio com erro volta a rascunho para corrigir e enviar de novo");
        g.MapPost("/{id:guid}/cancel", async (Guid id, IContractService contracts, CancellationToken ct) =>
                (await contracts.CancelAsync(id, ct)).ToHttp())
            .WithSummary("Cancela no sistema e remove no provedor");

        g.MapGet("/{id:guid}/events", async (Guid id, IContractService contracts, CancellationToken ct) =>
                (await contracts.GetEventsAsync(id, ct)).ToHttp())
            .WithSummary("Linha do tempo");
        g.MapGet("/{id:guid}/document", (Guid id, IContractService contracts, CancellationToken ct) => File(contracts, id, false, ct))
            .WithSummary("Baixa o PDF original");
        g.MapGet("/{id:guid}/signed-document", (Guid id, IContractService contracts, CancellationToken ct) => File(contracts, id, true, ct))
            .WithSummary("Baixa o PDF assinado");
        g.MapGet("/{id:guid}/preview", async (Guid id, IContractService contracts, HttpContext http, CancellationToken ct) =>
                (await contracts.PreviewAsync(id, ct)).ToHttp(html => TemplatesEndpoints.Html(http, html)))
            .WithSummary("HTML do contrato com os valores atuais");

        return api;
    }

    /// <summary>PDF passa pela API (que confere a organização); o bucket nunca fica exposto.</summary>
    private static async Task<IResult> File(IContractService contracts, Guid id, bool signed, CancellationToken ct) =>
        (await contracts.GetDocumentAsync(id, signed, ct)).ToHttp(f =>
            TypedResults.Stream(f.Content, "application/pdf", f.FileName));

    private static IResult WithETag(HttpContext http, ContractDetail c, IResult result)
    {
        ETags.Set(http, c.RowVersion);
        return result;
    }
}

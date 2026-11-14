using System.Net;
using Contratos.Infrastructure.Contracts;
using Contratos.Infrastructure.Persistence;
using Contratos.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contratos.Tests.Contracts;

public sealed partial class ContractsApiTests
{
    private static async Task<int> CountAsync(TestAccount account, string query)
    {
        var res = await account.Client.GetAsync($"/api/v1/contracts?{query}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.JsonAsync()).GetProperty("total").GetInt32();
    }

    [Theory]
    [InlineData("acao")]                 // sem acento encontra "Ação"
    [InlineData("Consul")]               // prefixo de palavra do título
    [InlineData("aurora")]               // valor de variável (contratante.nome = Café Aurora Ltda)
    [InlineData("cafe aurora")]          // várias palavras, sem acento
    [InlineData("joao")]                 // nome do signatário sem acento
    [InlineData("cliente.com")]          // trecho do e-mail do signatário
    [InlineData("arina cos")]            // trecho do meio do nome (trigram)
    [InlineData("0001")]                 // número com zeros
    [InlineData("#1")]
    public async Task Search_finds_contracts_by_title_values_signers_and_number(string term)
    {
        var (admin, id) = await DraftAsync("Consultoria de ação digital");
        await ReadyToSendAsync(admin, id, "Consultoria de ação digital");

        var res = await (await admin.Client.GetAsync($"/api/v1/contracts?q={Uri.EscapeDataString(term)}")).JsonAsync();

        Assert.Contains(res.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == id);
    }

    [Theory]
    [InlineData("ap 42")]
    [InlineData("AP-42")]
    [InlineData("2026")]
    [InlineData("15")]
    public async Task Search_finds_codes_with_hyphens_and_slashes(string term)
    {
        var (admin, id) = await DraftAsync("Apartamento AP-42 contrato CT-2026/15");

        var res = await (await admin.Client.GetAsync($"/api/v1/contracts?q={Uri.EscapeDataString(term)}")).JsonAsync();

        Assert.Contains(res.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == id);
    }

    [Theory]
    [InlineData("a & | ! :* ()")]
    [InlineData("'; DROP TABLE contracts; --")]
    [InlineData("%_\\")]
    public async Task Search_input_cannot_break_the_query(string term)
    {
        var (admin, _) = await DraftAsync();
        var res = await admin.Client.GetAsync($"/api/v1/contracts?q={Uri.EscapeDataString(term)}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Search_does_not_match_unrelated_terms()
    {
        var (admin, id) = await DraftAsync();
        await ReadyToSendAsync(admin, id);
        Assert.Equal(0, await CountAsync(admin, "q=xilofone"));
    }

    [Fact]
    public async Task Filters_by_completion_period_and_creator()
    {
        var (admin, id) = await DraftAsync();
        await ReadyToSendAsync(admin, id);
        await admin.Client.PostAsync($"/api/v1/contracts/{id}/send");
        await factory.ProcessBackgroundAsync();
        var detail = await GetAsync(admin, id);
        await SimulateAsync(admin, id, SignerId(detail, 0), "sign");
        await SimulateAsync(admin, id, SignerId(detail, 1), "sign");
        var templateId = detail.GetProperty("template").GetProperty("id").GetGuid();
        await admin.Client.PostAsync("/api/v1/contracts", new { templateId, title = "Outro rascunho" });

        var today = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-dd");
        Assert.Equal(1, await CountAsync(admin, $"dateField=completed&from={today}&to={today}"));
        Assert.Equal(2, await CountAsync(admin, $"from={today}&to={today}"));
        Assert.Equal(0, await CountAsync(admin, "dateField=completed&from=2020-01-01&to=2020-01-31"));

        var me = (await (await admin.Client.GetAsync("/api/v1/auth/me")).JsonAsync()).GetProperty("id").GetGuid();
        Assert.Equal(2, await CountAsync(admin, $"createdBy={me}"));
        Assert.Equal(0, await CountAsync(admin, $"createdBy={Guid.NewGuid()}"));

        var members = await (await admin.Client.GetAsync("/api/v1/organization/members")).JsonAsync();
        Assert.Contains(members.EnumerateArray(), m => m.GetProperty("id").GetGuid() == me);
    }

    [Fact]
    public async Task Dashboard_counts_statuses_and_highlights_old_pending_contracts()
    {
        var (admin, id) = await DraftAsync();
        await ReadyToSendAsync(admin, id);
        await admin.Client.PostAsync($"/api/v1/contracts/{id}/send");
        await factory.ProcessBackgroundAsync();
        var templateId = (await GetAsync(admin, id)).GetProperty("template").GetProperty("id").GetGuid();
        await admin.Client.PostAsync("/api/v1/contracts", new { templateId });

        var fresh = await (await admin.Client.GetAsync("/api/v1/dashboard/summary")).JsonAsync();
        Assert.Equal(1, fresh.GetProperty("counts").GetProperty("Draft").GetInt32());
        Assert.Equal(1, fresh.GetProperty("counts").GetProperty("Sent").GetInt32());
        Assert.Equal(1, fresh.GetProperty("open").GetInt32());
        Assert.Equal(1, fresh.GetProperty("sentThisMonth").GetInt32());
        Assert.Equal(0, fresh.GetProperty("stale").GetArrayLength());
        Assert.True(fresh.GetProperty("hasPublishedTemplate").GetBoolean());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlAsync($"UPDATE contracts SET sent_at = now() - interval '5 days' WHERE id = {id}");
        }

        var later = await (await admin.Client.GetAsync("/api/v1/dashboard/summary?staleDays=3")).JsonAsync();
        var stale = Assert.Single(later.GetProperty("stale").EnumerateArray());
        Assert.Equal(id, stale.GetProperty("id").GetGuid());
        Assert.Equal(5, stale.GetProperty("days").GetInt32());
        Assert.Equal(2, stale.GetProperty("signersTotal").GetInt32());
    }

    [Theory]
    [InlineData("café aur", "café:* & aur:*")]
    [InlineData("  a&b|c!  ", "a:* & b:* & c:*")]
    [InlineData("&|!:*()", null)]
    public void Builds_prefix_tsquery_only_from_letters_and_digits(string input, string? expected) =>
        Assert.Equal(expected, ContractService.PrefixTsQuery(input));
}

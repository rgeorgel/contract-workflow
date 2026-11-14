using System.Net;
using System.Text.Json;
using Contratos.Tests.Infrastructure;

namespace Contratos.Tests.Templates;

[Collection(ApiCollection.Name)]
public sealed class TemplatesApiTests(ApiFactory factory)
{
    private const string Body = "<h1>Contrato</h1><p>Contratante: {{contratante.nome}}, CPF {{contratante.cpf}}, valor {{valor}}.</p>";

    private static object Variables() => new object[]
    {
        new { key = "contratante.nome", label = "Nome do contratante", type = "Text", required = true },
        new { key = "contratante.cpf", label = "CPF do contratante", type = "CpfCnpj", required = true },
        new { key = "valor", label = "Valor", type = "Money", required = true },
    };

    private static object Roles() => new object[]
    {
        new { role = "Contratante", action = "Sign", order = 1 },
        new { role = "Testemunha", action = "SignAsWitness", order = 2 },
    };

    public static async Task<JsonElement> CreatePublishedAsync(TestAccount admin, string name = "Prestação de serviços")
    {
        var created = await (await admin.Client.PostAsync("/api/v1/templates", new { name })).JsonAsync();
        var id = created.GetProperty("id").GetGuid();
        var save = await admin.Client.PutAsync($"/api/v1/templates/{id}",
            new { name, bodyHtml = Body, variables = Variables(), defaultSignerRoles = Roles() });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var publish = await admin.Client.PostAsync($"/api/v1/templates/{id}/publish");
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        return await publish.JsonAsync();
    }

    [Fact]
    public async Task Publishing_freezes_a_version_and_editing_starts_the_next_one()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var v1 = await CreatePublishedAsync(admin);
        var id = v1.GetProperty("id").GetGuid();
        Assert.Equal(1, v1.GetProperty("current").GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, v1.GetProperty("draft").ValueKind);

        var edit = await (await admin.Client.PutAsync($"/api/v1/templates/{id}",
            new { name = "Prestação de serviços", bodyHtml = Body + "<p>Cláusula nova.</p>", variables = Variables(), defaultSignerRoles = Roles() })).JsonAsync();
        Assert.Equal(2, edit.GetProperty("draft").GetProperty("version").GetInt32());
        Assert.DoesNotContain("Cláusula nova", edit.GetProperty("current").GetProperty("bodyHtml").GetString());

        var v2 = await (await admin.Client.PostAsync($"/api/v1/templates/{id}/publish")).JsonAsync();
        Assert.Equal(2, v2.GetProperty("current").GetProperty("version").GetInt32());
        Assert.Equal(2, v2.GetProperty("versions").GetArrayLength());

        // A versão 1 continua lá, intacta.
        var v1Id = v2.GetProperty("versions").EnumerateArray().Single(v => v.GetProperty("version").GetInt32() == 1).GetProperty("id").GetGuid();
        var old = await (await admin.Client.GetAsync($"/api/v1/templates/{id}/versions/{v1Id}")).JsonAsync();
        Assert.DoesNotContain("Cláusula nova", old.GetProperty("bodyHtml").GetString());
    }

    [Fact]
    public async Task Cannot_publish_with_undeclared_variables()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var id = (await (await admin.Client.PostAsync("/api/v1/templates", new { name = "Incompleto" })).JsonAsync()).GetProperty("id").GetGuid();
        await admin.Client.PutAsync($"/api/v1/templates/{id}", new { name = "Incompleto", bodyHtml = "<p>{{faltando}}</p>", variables = Array.Empty<object>() });

        var res = await admin.Client.PostAsync($"/api/v1/templates/{id}/publish");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var message = (await res.JsonAsync()).GetProperty("errors").GetProperty("bodyHtml")[0].GetString();
        Assert.Contains("faltando", message);
    }

    [Fact]
    public async Task Html_is_sanitized_and_external_urls_are_removed()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var id = (await (await admin.Client.PostAsync("/api/v1/templates", new { name = "Sujo" })).JsonAsync()).GetProperty("id").GetGuid();
        const string dirty = """
            <p onclick="alert(1)">Texto</p><script>alert(2)</script>
            <img src="http://169.254.169.254/latest"><img src="data:image/png;base64,iVBORw0KGgo=">
            <p style="background:url(http://minio:9000/x)">Fundo</p><a href="https://exemplo.com.br">site</a>
            """;

        var saved = await (await admin.Client.PutAsync($"/api/v1/templates/{id}", new { name = "Sujo", bodyHtml = dirty })).JsonAsync();
        var html = saved.GetProperty("draft").GetProperty("bodyHtml").GetString()!;

        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("169.254", html);
        Assert.DoesNotContain("minio", html);
        Assert.Contains("data:image/png;base64", html);
        Assert.Contains("https://exemplo.com.br", html);
    }

    [Fact]
    public async Task Stale_if_match_is_rejected()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var created = await admin.Client.PostAsync("/api/v1/templates", new { name = "Concorrência" });
        var etag = created.Headers.ETag!.Tag;
        var id = (await created.JsonAsync()).GetProperty("id").GetGuid();

        var first = await admin.Client.PutAsync($"/api/v1/templates/{id}", new { name = "Primeira" }, ifMatch: etag);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await admin.Client.PutAsync($"/api/v1/templates/{id}", new { name = "Segunda" }, ifMatch: etag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, second.StatusCode);
    }

    [Fact]
    public async Task Preview_renders_sample_values_with_its_own_csp()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var id = (await CreatePublishedAsync(admin)).GetProperty("id").GetGuid();

        var res = await admin.Client.GetAsync($"/api/v1/templates/{id}/preview?source=current");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Nome do contratante", html);
        Assert.Contains("529.982.247-25", html);
        Assert.Contains("R$", html);
        Assert.Contains("default-src 'none'", string.Join(';', res.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("SAMEORIGIN", res.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Duplicate_and_archive()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var id = (await CreatePublishedAsync(admin, "Original")).GetProperty("id").GetGuid();

        var copy = await (await admin.Client.PostAsync($"/api/v1/templates/{id}/duplicate")).JsonAsync();
        Assert.Equal("Cópia de Original", copy.GetProperty("name").GetString());
        Assert.Equal(3, copy.GetProperty("draft").GetProperty("variables").GetArrayLength());

        await admin.Client.PostAsync($"/api/v1/templates/{id}/archive");
        var active = await (await admin.Client.GetAsync("/api/v1/templates")).JsonAsync();
        var archived = await (await admin.Client.GetAsync("/api/v1/templates?archived=true")).JsonAsync();
        Assert.DoesNotContain(active.EnumerateArray(), t => t.GetProperty("id").GetGuid() == id);
        Assert.Contains(archived.EnumerateArray(), t => t.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Editors_read_templates_but_only_admins_change_them()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var id = (await CreatePublishedAsync(admin)).GetProperty("id").GetGuid();
        var editor = await TestAccounts.InviteAndAcceptAsync(factory, admin, "Editor");

        Assert.Equal(HttpStatusCode.OK, (await editor.Client.GetAsync($"/api/v1/templates/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.Client.PostAsync("/api/v1/templates", new { name = "Não pode" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.Client.PostAsync($"/api/v1/templates/{id}/archive")).StatusCode);
    }

    [Fact]
    public async Task Templates_of_another_organization_are_invisible()
    {
        var adminA = await TestAccounts.CreateAdminAsync(factory);
        var published = await CreatePublishedAsync(adminA, "Segredo A");
        var id = published.GetProperty("id").GetGuid();
        var versionId = published.GetProperty("current").GetProperty("id").GetGuid();
        var adminB = await TestAccounts.CreateAdminAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await adminB.Client.GetAsync($"/api/v1/templates/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.Client.GetAsync($"/api/v1/templates/{id}/versions/{versionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.Client.GetAsync($"/api/v1/templates/{id}/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.Client.PutAsync($"/api/v1/templates/{id}", new { name = "Invasão" })).StatusCode);
        var list = await (await adminB.Client.GetAsync("/api/v1/templates")).JsonAsync();
        Assert.DoesNotContain(list.EnumerateArray(), t => t.GetProperty("id").GetGuid() == id);
    }
}

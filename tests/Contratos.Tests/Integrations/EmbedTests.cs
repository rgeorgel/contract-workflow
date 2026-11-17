using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Contratos.Infrastructure.Integrations;
using Contratos.Tests.Infrastructure;
using Contratos.Tests.Templates;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Contratos.Tests.Integrations;

[Collection(ApiCollection.Name)]
public sealed class EmbedTests(ApiFactory factory)
{
    private const string Crm = "https://crm.cliente.com.br";
    private const string Body = "<h1>Contrato</h1><p>Contratante: {{contratante.nome}}.</p>";
    private static readonly object[] Variables = [new { key = "contratante.nome", label = "Nome", type = "Text", required = true }];

    // ---------- Apoio ----------

    private HttpClient Client(string? bearer = null)
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (bearer is not null)
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return http;
    }

    private sealed record Setup(TestAccount Admin, HttpClient Api, string Key);

    private async Task<Setup> SetupAsync(bool withOrigin = true)
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var created = await (await admin.Client.PostAsync("/api/v1/integrations/keys", new { name = "CRM" })).JsonAsync();
        var key = created.GetProperty("token").GetString()!;
        if (withOrigin)
            Assert.Equal(HttpStatusCode.Created, (await admin.Client.PostAsync("/api/v1/integrations/embed-origins", new { origin = Crm + "/" })).StatusCode);
        return new Setup(admin, Client(key), key);
    }

    private static async Task<JsonElement> CreateSessionAsync(HttpClient api, object? body = null)
    {
        var res = await api.PostAsJsonAsync("/api/public/v1/embed/sessions", body ?? new { });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.JsonAsync();
    }

    /// <summary>O que a página do iframe faz: lê o id e o token do link e abre a sessão.</summary>
    private async Task<string> OpenAsync(JsonElement session)
    {
        var url = new Uri(session.GetProperty("url").GetString()!);
        var token = url.Fragment["#token=".Length..];
        var id = System.Web.HttpUtility.ParseQueryString(url.Query)["session"];
        var res = await Client().PostAsJsonAsync("/api/embed/v1/session", new { sessionId = id, token });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.JsonAsync()).GetProperty("accessToken").GetString()!;
    }

    private async Task<HttpClient> EmbedAsync(HttpClient api, object? body = null) =>
        Client(await OpenAsync(await CreateSessionAsync(api, body)));

    private static async Task<string?> CodeAsync(HttpResponseMessage res) =>
        (await res.JsonAsync()).TryGetProperty("code", out var c) ? c.GetString() : null;

    // ---------- Domínios ----------

    [Theory]
    [InlineData("https://crm.cliente.com.br/", "https://crm.cliente.com.br")]
    [InlineData("HTTPS://CRM.Cliente.com.br:8443", "https://crm.cliente.com.br:8443")]
    [InlineData("https://*.cliente.com.br", "https://*.cliente.com.br")]
    [InlineData("http://localhost:3000", "http://localhost:3000")]
    [InlineData("http://127.0.0.1:5173", "http://127.0.0.1:5173")]
    public void Origins_are_normalized(string input, string expected) =>
        Assert.Equal(expected, EmbedOrigins.Normalize(input).Origin);

    [Theory]
    [InlineData("")]
    [InlineData("crm.cliente.com.br")]
    [InlineData("http://crm.cliente.com.br")]           // http só em localhost
    [InlineData("https://crm.cliente.com.br/app")]      // sem caminho
    [InlineData("https://crm.cliente.com.br?x=1")]
    [InlineData("https://user:senha@crm.cliente.com.br")]
    [InlineData("https://*.com")]                       // liberaria qualquer site .com
    [InlineData("https://intranet")]
    [InlineData("ftp://crm.cliente.com.br")]
    [InlineData("https://crm.cliente.com.br; script-src *")]
    public void Invalid_origins_are_rejected(string input) =>
        Assert.NotNull(EmbedOrigins.Normalize(input).Error);

    [Theory]
    [InlineData("https://crm.cliente.com.br", "https://crm.cliente.com.br", true)]
    [InlineData("https://*.cliente.com.br", "https://loja.cliente.com.br", true)]
    [InlineData("https://*.cliente.com.br", "https://a.b.cliente.com.br", true)]
    [InlineData("https://*.cliente.com.br", "https://cliente.com.br", false)]
    [InlineData("https://*.cliente.com.br", "https://malicioso-cliente.com.br", false)]
    [InlineData("https://*.cliente.com.br", "http://loja.cliente.com.br", false)]
    [InlineData("https://crm.cliente.com.br", "https://crm.cliente.com.br:8443", false)]
    public void Origin_patterns_match_like_frame_ancestors(string pattern, string origin, bool expected) =>
        Assert.Equal(expected, EmbedOrigins.Matches(pattern, origin));

    [Fact]
    public async Task Sessions_need_a_registered_site_and_valid_options()
    {
        var (admin, api, _) = await SetupAsync(withOrigin: false);
        var noOrigin = await api.PostAsJsonAsync("/api/public/v1/embed/sessions", new { });
        Assert.Equal(HttpStatusCode.Conflict, noOrigin.StatusCode);
        Assert.Equal("embed_origin_required", await CodeAsync(noOrigin));

        Assert.Equal(HttpStatusCode.Created, (await admin.Client.PostAsync("/api/v1/integrations/embed-origins", new { origin = Crm })).StatusCode);
        var duplicate = await admin.Client.PostAsync("/api/v1/integrations/embed-origins", new { origin = Crm + "/" });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var invalid = await api.PostAsJsonAsync("/api/public/v1/embed/sessions", new
        {
            permissions = new[] { "publish", "delete" },
            parentOrigin = "https://outro-site.com.br",
            expiresInMinutes = 2,
            templateId = Guid.NewGuid(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await invalid.JsonAsync()).GetProperty("errors");
        foreach (var field in new[] { "permissions", "parentOrigin", "expiresInMinutes", "templateId" })
            Assert.True(errors.TryGetProperty(field, out _), field);

        // Corpo vazio: todas as permissões e 60 minutos.
        var empty = await api.PostAsync("/api/public/v1/embed/sessions", null);
        Assert.Equal(HttpStatusCode.Created, empty.StatusCode);
        var session = await empty.JsonAsync();
        Assert.Equal(["edit", "publish", "archive"], session.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        var url = session.GetProperty("url").GetString()!;
        Assert.Matches("/embed/templates\\?session=[0-9a-f-]{36}#token=cel_[A-Za-z0-9_-]{40,}$", url);
        var expires = session.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expires - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(61));
    }

    // ---------- Abertura e autenticação ----------

    [Fact]
    public async Task The_link_opens_once_and_the_access_token_only_works_on_the_embed_api()
    {
        var (admin, api, key) = await SetupAsync();
        var session = await CreateSessionAsync(api, new { user = new { id = "u-7", name = "Maria Souza" } });
        var access = await OpenAsync(session);
        Assert.StartsWith("ces_", access);

        // Segunda abertura do mesmo link: não vale (o token do link é de uso único).
        var url = new Uri(session.GetProperty("url").GetString()!);
        var again = await Client().PostAsJsonAsync("/api/embed/v1/session",
            new { sessionId = session.GetProperty("id").GetGuid(), token = url.Fragment["#token=".Length..] });
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        Assert.Equal("embed_link_invalid", await CodeAsync(again));

        var embed = Client(access);
        var info = await (await embed.GetAsync("/api/embed/v1/session")).JsonAsync();
        Assert.Equal("Maria Souza", info.GetProperty("user").GetProperty("name").GetString());
        Assert.Equal([Crm], info.GetProperty("allowedOrigins").EnumerateArray().Select(o => o.GetString()));
        Assert.Equal(HttpStatusCode.OK, (await embed.GetAsync("/api/embed/v1/templates")).StatusCode);

        // Cada credencial só na sua API.
        Assert.Equal(HttpStatusCode.Unauthorized, (await embed.GetAsync("/api/public/v1/account")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await embed.GetAsync("/api/v1/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(key).GetAsync("/api/embed/v1/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.Client.GetAsync("/api/embed/v1/templates")).StatusCode);
        var anonymous = await Client().GetAsync("/api/embed/v1/templates");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("embed_session_expired", await CodeAsync(anonymous));
        Assert.Equal("route_not_found", await CodeAsync(await embed.GetAsync("/api/embed/v1/contracts")));
    }

    [Fact]
    public async Task Ending_revoking_or_losing_the_key_closes_the_session()
    {
        var (admin, api, _) = await SetupAsync();

        var ended = await EmbedAsync(api);
        Assert.Equal(HttpStatusCode.NoContent, (await ended.DeleteAsync("/api/embed/v1/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ended.GetAsync("/api/embed/v1/templates")).StatusCode);

        var session = await CreateSessionAsync(api);
        var revoked = Client(await OpenAsync(session));
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/api/public/v1/embed/sessions/{session.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await revoked.GetAsync("/api/embed/v1/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.DeleteAsync($"/api/public/v1/embed/sessions/{Guid.NewGuid()}")).StatusCode);

        var withKey = await EmbedAsync(api);
        var keyId = (await (await admin.Client.GetAsync("/api/v1/integrations")).JsonAsync()).GetProperty("keys")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.DeleteAsync($"/api/v1/integrations/keys/{keyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await withKey.GetAsync("/api/embed/v1/templates")).StatusCode);
    }

    [Fact]
    public async Task Sessions_from_one_organization_do_not_see_another()
    {
        var (other, _, _) = await SetupAsync();
        var foreign = (await TemplatesApiTests.CreatePublishedAsync(other, "Template de outra empresa")).GetProperty("id").GetGuid();
        var (_, api, _) = await SetupAsync();
        var embed = await EmbedAsync(api);
        Assert.Empty((await (await embed.GetAsync("/api/embed/v1/templates")).JsonAsync()).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await embed.GetAsync($"/api/embed/v1/templates/{foreign}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await embed.PostAsync($"/api/embed/v1/templates/{foreign}/preview-link", null)).StatusCode);
    }

    // ---------- Templates pelo embed ----------

    [Fact]
    public async Task The_embed_manages_templates_within_the_session_permissions()
    {
        var (admin, api, _) = await SetupAsync();

        var readOnly = await EmbedAsync(api, new { permissions = Array.Empty<string>() });
        var denied = await readOnly.PostAsJsonAsync("/api/embed/v1/templates", new { name = "Não pode" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("embed_permission_denied", await CodeAsync(denied));

        var editor = await EmbedAsync(api, new { permissions = new[] { "edit" }, user = new { name = "Maria Souza" } });
        var created = await editor.PostAsJsonAsync("/api/embed/v1/templates", new { name = "Locação" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.JsonAsync()).GetProperty("id").GetGuid();
        var saved = await editor.PutAsJsonAsync($"/api/embed/v1/templates/{id}",
            new { name = "Locação", bodyHtml = Body, variables = Variables, defaultSignerRoles = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.NotNull(saved.Headers.ETag);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsync($"/api/embed/v1/templates/{id}/publish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsync($"/api/embed/v1/templates/{id}/archive", null)).StatusCode);

        var full = await EmbedAsync(api, new { user = new { name = "João Lima" } });
        var published = await full.PostAsync($"/api/embed/v1/templates/{id}/publish", null);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);

        // Na tela, o histórico mostra quem criou pela integração; o template já aparece na API pública.
        var detail = await (await admin.Client.GetAsync($"/api/v1/templates/{id}")).JsonAsync();
        Assert.Equal("Maria Souza (pela integração)", detail.GetProperty("versions")[0].GetProperty("createdByName").GetString());
        var list = await (await api.GetAsync("/api/public/v1/templates")).JsonAsync();
        Assert.Contains(list.EnumerateArray(), t => t.GetProperty("id").GetGuid() == id);
        Assert.Equal(HttpStatusCode.OK, (await full.PostAsync($"/api/embed/v1/templates/{id}/archive", null)).StatusCode);
    }

    [Fact]
    public async Task Publishing_and_archiving_in_the_embed_reach_the_webhooks()
    {
        var (admin, api, _) = await SetupAsync();
        var url = $"https://crm.test/hooks/{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.Created, (await admin.Client.PostAsync("/api/v1/integrations/webhooks",
            new { url, events = new[] { "template.published", "template.archived" } })).StatusCode);

        var embed = await EmbedAsync(api);
        var id = (await (await embed.PostAsJsonAsync("/api/embed/v1/templates", new { name = "Locação" })).JsonAsync()).GetProperty("id").GetGuid();
        await embed.PutAsJsonAsync($"/api/embed/v1/templates/{id}", new { name = "Locação", bodyHtml = Body, variables = Variables, defaultSignerRoles = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.OK, (await embed.PostAsync($"/api/embed/v1/templates/{id}/publish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await embed.PostAsync($"/api/embed/v1/templates/{id}/archive", null)).StatusCode);
        await factory.ProcessBackgroundAsync();

        var received = factory.Webhooks.To(url);
        Assert.Equal(["template.published", "template.archived"], received.Select(r => r.Header("X-Contratos-Event")));
        var template = JsonDocument.Parse(received[0].Body).RootElement.GetProperty("data").GetProperty("template");
        Assert.Equal(id, template.GetProperty("id").GetGuid());
        Assert.Equal(1, template.GetProperty("version").GetInt32());
        Assert.Equal("text", template.GetProperty("variables")[0].GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(received[0].Body).RootElement.GetProperty("data").GetProperty("contract").ValueKind);
    }

    // ---------- Páginas e pré-visualização ----------

    [Fact]
    public async Task Embed_pages_can_only_be_framed_by_the_registered_sites()
    {
        var (admin, api, _) = await SetupAsync();
        Assert.Equal(HttpStatusCode.Created, (await admin.Client.PostAsync("/api/v1/integrations/embed-origins", new { origin = "https://*.cliente.com.br" })).StatusCode);
        var session = await CreateSessionAsync(api);
        var id = session.GetProperty("id").GetGuid();

        var page = await Client().GetAsync($"/embed/templates?session={id}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("<h1>Templates</h1>", await page.Content.ReadAsStringAsync());
        var csp = page.Headers.GetValues("Content-Security-Policy").Single();
        Assert.EndsWith($"frame-ancestors {Crm} https://*.cliente.com.br", csp);
        Assert.Contains("script-src 'self'", csp);
        Assert.False(page.Headers.Contains("X-Frame-Options"));

        // Site exato informado na sessão: só ele.
        var exact = await CreateSessionAsync(api, new { parentOrigin = "https://loja.cliente.com.br" });
        var editor = await Client().GetAsync($"/embed/template-editor?session={exact.GetProperty("id").GetGuid()}&id={Guid.NewGuid()}");
        Assert.EndsWith("frame-ancestors https://loja.cliente.com.br", editor.Headers.GetValues("Content-Security-Policy").Single());

        // Sessão que não existe, e as páginas normais da tela: ninguém emoldura.
        var unknown = await Client().GetAsync($"/embed/templates?session={Guid.NewGuid()}");
        Assert.EndsWith("frame-ancestors 'none'", unknown.Headers.GetValues("Content-Security-Policy").Single());
        var normal = await Client().GetAsync("/templates.html");
        Assert.EndsWith("frame-ancestors 'none'", normal.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("DENY", normal.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Preview_opens_with_a_short_ticket_for_that_template_only()
    {
        var (admin, api, _) = await SetupAsync();
        var templateId = (await TemplatesApiTests.CreatePublishedAsync(admin)).GetProperty("id").GetGuid();
        var otherId = (await TemplatesApiTests.CreatePublishedAsync(admin, "Outro")).GetProperty("id").GetGuid();
        var embed = await EmbedAsync(api);

        var link = await (await embed.PostAsync($"/api/embed/v1/templates/{templateId}/preview-link?source=current", null)).JsonAsync();
        var url = link.GetProperty("url").GetString()!;
        Assert.StartsWith($"/api/embed/v1/templates/{templateId}/preview?source=current&ticket=", url);

        var preview = await Client().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains("Contrato", await preview.Content.ReadAsStringAsync());
        Assert.EndsWith($"frame-ancestors 'self' {Crm}", preview.Headers.GetValues("Content-Security-Policy").Single());
        Assert.False(preview.Headers.Contains("X-Frame-Options"));

        // O ticket não abre outro template nem outra rota.
        var ticket = url[(url.IndexOf("ticket=", StringComparison.Ordinal) + 7)..];
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync($"/api/embed/v1/templates/{otherId}/preview?ticket={ticket}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync($"/api/embed/v1/templates/{templateId}?ticket={ticket}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync($"/api/embed/v1/templates/{templateId}/preview?ticket=x{ticket}")).StatusCode);
    }

    [Fact]
    public async Task Integrations_overview_lists_and_removes_embed_sites()
    {
        var (admin, _, _) = await SetupAsync();
        var overview = await (await admin.Client.GetAsync("/api/v1/integrations")).JsonAsync();
        var origin = Assert.Single(overview.GetProperty("embedOrigins").EnumerateArray());
        Assert.Equal(Crm, origin.GetProperty("origin").GetString());
        Assert.Equal(10, overview.GetProperty("maxEmbedOrigins").GetInt32());

        var invalid = await admin.Client.PostAsync("/api/v1/integrations/embed-origins", new { origin = "http://crm.cliente.com.br" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.True((await invalid.JsonAsync()).GetProperty("errors").TryGetProperty("origin", out _));

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.Client.DeleteAsync($"/api/v1/integrations/embed-origins/{origin.GetProperty("id").GetGuid()}")).StatusCode);
        var editor = await TestAccounts.InviteAndAcceptAsync(factory, admin, "Editor");
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.Client.PostAsync("/api/v1/integrations/embed-origins", new { origin = Crm })).StatusCode);
    }
}

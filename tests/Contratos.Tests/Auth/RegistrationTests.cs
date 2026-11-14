using System.Net;
using System.Text.RegularExpressions;
using Contratos.Domain.Users;
using Contratos.Infrastructure.Persistence;
using Contratos.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contratos.Tests.Auth;

[Collection(ApiCollection.Name)]
public sealed partial class RegistrationTests(ApiFactory factory)
{
    private const string Password = "senha-bem-longa-123";

    private static string NewEmail() => $"pessoa-{Guid.NewGuid():N}@empresa.com.br";

    private static object Registration(string email, string org = "Café Aurora Ltda", string password = Password) =>
        new { organizationName = org, name = "Marina Costa", email, password };

    private async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private (Guid UserId, string Token) ConfirmationFrom(string email)
    {
        var message = factory.Emails.To(email).Last(m => m.Subject.StartsWith("Confirme", StringComparison.Ordinal));
        var match = ConfirmLink().Match(message.TextBody);
        Assert.True(match.Success, "link de confirmação não encontrado no e-mail");
        return (Guid.Parse(match.Groups["user"].Value), match.Groups["token"].Value);
    }

    [GeneratedRegex(@"https://app\.test/signup\.html\?step=confirm&userId=(?<user>[0-9a-f-]+)&token=(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex ConfirmLink();

    [Fact]
    public async Task Register_creates_organization_and_admin_and_sends_confirmation()
    {
        var client = factory.CreateApiClient();
        var email = NewEmail();

        var res = await client.PostAsync("/api/v1/auth/register", Registration(email));

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var user = await WithDb(db => db.Users.SingleAsync(u => u.Email == email));
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.False(user.EmailConfirmed);
        Assert.Equal(7, user.Id.Version);

        var org = await WithDb(db => db.Organizations.SingleAsync(o => o.Id == user.OrganizationId));
        Assert.Equal("Café Aurora Ltda", org.Name);
        Assert.StartsWith("cafe-aurora-ltda", org.Slug, StringComparison.Ordinal);

        var (userId, _) = ConfirmationFrom(email);
        Assert.Equal(user.Id, userId);
    }

    [Fact]
    public async Task Full_flow_confirm_email_then_login_then_me()
    {
        var client = factory.CreateApiClient();
        var email = NewEmail();
        await client.PostAsync("/api/v1/auth/register", Registration(email, org: "Fluxo Completo SA"));

        var beforeConfirm = await client.PostAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Forbidden, beforeConfirm.StatusCode);
        Assert.Equal("email_not_confirmed", (await beforeConfirm.JsonAsync()).GetProperty("code").GetString());

        var (userId, token) = ConfirmationFrom(email);
        var confirm = await client.PostAsync("/api/v1/auth/confirm-email", new { userId, token });
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        var login = await client.PostAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var me = await (await client.GetAsync("/api/v1/auth/me")).JsonAsync();
        Assert.Equal(email, me.GetProperty("email").GetString());
        Assert.Equal("Admin", me.GetProperty("role").GetString());
        Assert.Equal("Fluxo Completo SA", me.GetProperty("organization").GetProperty("name").GetString());

        var logout = await client.PostAsync("/api/v1/auth/logout");
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Register_with_confirmed_email_answers_the_same_and_warns_the_owner()
    {
        var client = factory.CreateApiClient();
        var email = NewEmail();
        await client.PostAsync("/api/v1/auth/register", Registration(email));
        var (userId, token) = ConfirmationFrom(email);
        await client.PostAsync("/api/v1/auth/confirm-email", new { userId, token });

        var again = await client.PostAsync("/api/v1/auth/register", Registration(email, org: "Outra Empresa"));

        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Contains(factory.Emails.To(email), m => m.Subject.StartsWith("Você já tem uma conta", StringComparison.Ordinal));
        Assert.False(await WithDb(db => db.Organizations.AnyAsync(o => o.Name == "Outra Empresa")));
    }

    [Fact]
    public async Task Register_over_unconfirmed_account_discards_the_earlier_attempt()
    {
        var client = factory.CreateApiClient();
        var email = NewEmail();
        await client.PostAsync("/api/v1/auth/register", Registration(email, org: "Tentativa Um", password: "senha-do-intruso-1"));
        await client.PostAsync("/api/v1/auth/register", Registration(email, org: "Tentativa Dois", password: Password));

        Assert.False(await WithDb(db => db.Organizations.AnyAsync(o => o.Name == "Tentativa Um")));
        Assert.Equal(1, await WithDb(db => db.Users.CountAsync(u => u.Email == email)));

        var (userId, token) = ConfirmationFrom(email);
        await client.PostAsync("/api/v1/auth/confirm-email", new { userId, token });

        var oldPassword = await client.PostAsync("/api/v1/auth/login", new { email, password = "senha-do-intruso-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        var newPassword = await client.PostAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.NoContent, newPassword.StatusCode);
    }

    [Fact]
    public async Task Register_validates_fields()
    {
        var client = factory.CreateApiClient();

        var res = await client.PostAsync("/api/v1/auth/register",
            new { organizationName = "", name = "M", email = "nao-e-email", password = "curta" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await res.JsonAsync()).GetProperty("errors");
        foreach (var field in new[] { "organizationName", "name", "email", "password" })
            Assert.True(errors.TryGetProperty(field, out _), $"faltou erro para {field}");
    }

    [Theory]
    [InlineData("12345", HttpStatusCode.BadRequest)]
    [InlineData("123456", HttpStatusCode.Accepted)]
    public async Task Password_needs_at_least_six_characters(string password, HttpStatusCode expected)
    {
        var client = factory.CreateApiClient();

        var res = await client.PostAsync("/api/v1/auth/register", Registration(NewEmail(), password: password));

        Assert.Equal(expected, res.StatusCode);
    }

    [Fact]
    public async Task Same_organization_name_gets_distinct_slugs()
    {
        var client = factory.CreateApiClient();
        await client.PostAsync("/api/v1/auth/register", Registration(NewEmail(), org: "Slug Repetido"));
        await client.PostAsync("/api/v1/auth/register", Registration(NewEmail(), org: "Slug Repetido"));

        var slugs = await WithDb(db => db.Organizations.Where(o => o.Name == "Slug Repetido").Select(o => o.Slug).ToListAsync());
        Assert.Equal(2, slugs.Distinct().Count());
    }

    [Fact]
    public async Task Invalid_confirmation_token_is_rejected()
    {
        var client = factory.CreateApiClient();
        var email = NewEmail();
        await client.PostAsync("/api/v1/auth/register", Registration(email));
        var (userId, _) = ConfirmationFrom(email);

        var res = await client.PostAsync("/api/v1/auth/confirm-email", new { userId, token = "dG9rZW4tZmFsc28" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("invalid_token", (await res.JsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Resend_confirmation_does_not_reveal_unknown_emails()
    {
        var client = factory.CreateApiClient();
        var unknown = NewEmail();

        var res = await client.PostAsync("/api/v1/auth/resend-confirmation", new { email = unknown });

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.Empty(factory.Emails.To(unknown));
    }
}

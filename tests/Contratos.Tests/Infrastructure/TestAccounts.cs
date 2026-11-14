using System.Net;
using System.Text.RegularExpressions;

namespace Contratos.Tests.Infrastructure;

public sealed record TestAccount(ApiClient Client, string Email, string Password, string OrganizationName);

public static partial class TestAccounts
{
    public const string Password = "senha-bem-longa-123";

    public static string NewEmail(string prefix = "pessoa") => $"{prefix}-{Guid.NewGuid():N}@empresa.com.br";

    /// <summary>Cadastra uma organização, confirma o e-mail e devolve o Admin já logado.</summary>
    public static async Task<TestAccount> CreateAdminAsync(ApiFactory factory, string? organizationName = null)
    {
        var client = factory.CreateApiClient();
        var email = NewEmail("admin");
        var org = organizationName ?? $"Empresa {Guid.NewGuid():N}"[..20];

        var res = await client.PostAsync("/api/v1/auth/register",
            new { organizationName = org, name = "Admin Teste", email, password = Password });
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        var (userId, token) = LinkParams(factory, email, "/signup.html?step=confirm");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/confirm-email", new { userId, token })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/login", new { email, password = Password })).StatusCode);
        return new TestAccount(client, email, Password, org);
    }

    /// <summary>Convida alguém com o perfil dado, aceita o convite e devolve a pessoa logada.</summary>
    public static async Task<TestAccount> InviteAndAcceptAsync(ApiFactory factory, TestAccount admin, string role)
    {
        var email = NewEmail(role.ToLowerInvariant());
        var res = await admin.Client.PostAsync("/api/v1/users/invite", new { email, role });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var (userId, token) = LinkParams(factory, email, "/invite.html");
        var client = factory.CreateApiClient();
        var accept = await client.PostAsync("/api/v1/auth/accept-invite", new { userId, token, name = $"{role} Teste", password = Password });
        Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        return new TestAccount(client, email, Password, admin.OrganizationName);
    }

    /// <summary>userId e token do último e-mail enviado para o endereço com um link para <paramref name="path"/>.</summary>
    public static (Guid UserId, string Token) LinkParams(ApiFactory factory, string email, string path)
    {
        var message = factory.Emails.To(email).Last(m => m.TextBody.Contains(path, StringComparison.Ordinal));
        var match = Link().Match(message.TextBody[message.TextBody.IndexOf(path, StringComparison.Ordinal)..]);
        Assert.True(match.Success, $"link {path} não encontrado no e-mail");
        return (Guid.Parse(match.Groups["user"].Value), match.Groups["token"].Value);
    }

    [GeneratedRegex(@"userId=(?<user>[0-9a-f-]+)&token=(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex Link();
}

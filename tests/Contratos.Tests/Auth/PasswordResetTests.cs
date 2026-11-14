using System.Net;
using Contratos.Tests.Infrastructure;

namespace Contratos.Tests.Auth;

[Collection(ApiCollection.Name)]
public sealed class PasswordResetTests(ApiFactory factory)
{
    [Fact]
    public async Task Reset_link_sets_a_new_password()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var anon = factory.CreateApiClient();

        Assert.Equal(HttpStatusCode.Accepted, (await anon.PostAsync("/api/v1/auth/forgot-password", new { email = admin.Email })).StatusCode);
        var (userId, token) = TestAccounts.LinkParams(factory, admin.Email, "/reset-password.html");

        var reset = await anon.PostAsync("/api/v1/auth/reset-password", new { userId, token, password = "nova-senha" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var oldLogin = await anon.PostAsync("/api/v1/auth/login", new { email = admin.Email, password = admin.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        var newLogin = await anon.PostAsync("/api/v1/auth/login", new { email = admin.Email, password = "nova-senha" });
        Assert.Equal(HttpStatusCode.NoContent, newLogin.StatusCode);

        // O link não serve duas vezes.
        var again = await anon.PostAsync("/api/v1/auth/reset-password", new { userId, token, password = "outra-senha" });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task Forgot_password_does_not_reveal_unknown_emails()
    {
        var unknown = TestAccounts.NewEmail();
        var res = await factory.CreateApiClient().PostAsync("/api/v1/auth/forgot-password", new { email = unknown });

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.Empty(factory.Emails.To(unknown));
    }
}

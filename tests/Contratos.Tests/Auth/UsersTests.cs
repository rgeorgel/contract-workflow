using System.Net;
using Contratos.Tests.Infrastructure;

namespace Contratos.Tests.Auth;

[Collection(ApiCollection.Name)]
public sealed class UsersTests(ApiFactory factory)
{
    [Fact]
    public async Task Invited_person_accepts_and_lands_in_the_same_organization_with_the_chosen_role()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);

        var editor = await TestAccounts.InviteAndAcceptAsync(factory, admin, "Editor");

        var me = await (await editor.Client.GetAsync("/api/v1/auth/me")).JsonAsync();
        Assert.Equal("Editor", me.GetProperty("role").GetString());
        Assert.Equal(admin.OrganizationName, me.GetProperty("organization").GetProperty("name").GetString());

        var list = await (await admin.Client.GetAsync("/api/v1/users")).JsonAsync();
        Assert.Contains(list.EnumerateArray(), u => u.GetProperty("email").GetString() == editor.Email
            && u.GetProperty("status").GetString() == "Ativo");
    }

    [Fact]
    public async Task Invite_link_works_only_once()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var email = TestAccounts.NewEmail();
        await admin.Client.PostAsync("/api/v1/users/invite", new { email, role = "Leitor" });
        var (userId, token) = TestAccounts.LinkParams(factory, email, "/invite.html");

        var anon = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsync("/api/v1/auth/invite/lookup", new { userId, token })).StatusCode);
        await anon.PostAsync("/api/v1/auth/accept-invite", new { userId, token, name = "Leitora", password = "123456" });

        var again = await factory.CreateApiClient().PostAsync("/api/v1/auth/accept-invite", new { userId, token, name = "Outra", password = "abcdef" });
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Only_admins_manage_users()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var editor = await TestAccounts.InviteAndAcceptAsync(factory, admin, "Editor");

        Assert.Equal(HttpStatusCode.Forbidden, (await editor.Client.GetAsync("/api/v1/users")).StatusCode);
        var invite = await editor.Client.PostAsync("/api/v1/users/invite", new { email = TestAccounts.NewEmail(), role = "Admin" });
        Assert.Equal(HttpStatusCode.Forbidden, invite.StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_loses_access_and_cannot_log_in()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var editor = await TestAccounts.InviteAndAcceptAsync(factory, admin, "Editor");
        var editorId = (await (await editor.Client.GetAsync("/api/v1/auth/me")).JsonAsync()).GetProperty("id").GetGuid();

        var res = await admin.Client.PutAsync($"/api/v1/users/{editorId}", new { active = false });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await editor.Client.GetAsync("/api/v1/auth/me")).StatusCode);
        var login = await factory.CreateApiClient().PostAsync("/api/v1/auth/login", new { email = editor.Email, password = editor.Password });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal("account_disabled", (await login.JsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Admin_cannot_change_own_access()
    {
        var admin = await TestAccounts.CreateAdminAsync(factory);
        var adminId = (await (await admin.Client.GetAsync("/api/v1/auth/me")).JsonAsync()).GetProperty("id").GetGuid();

        var res = await admin.Client.PutAsync($"/api/v1/users/{adminId}", new { role = "Leitor" });

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_touch_users_of_another_organization()
    {
        var adminA = await TestAccounts.CreateAdminAsync(factory);
        var editorA = await TestAccounts.InviteAndAcceptAsync(factory, adminA, "Editor");
        var editorAId = (await (await editorA.Client.GetAsync("/api/v1/auth/me")).JsonAsync()).GetProperty("id").GetGuid();
        var adminB = await TestAccounts.CreateAdminAsync(factory);

        var update = await adminB.Client.PutAsync($"/api/v1/users/{editorAId}", new { active = false });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        var list = await (await adminB.Client.GetAsync("/api/v1/users")).JsonAsync();
        Assert.DoesNotContain(list.EnumerateArray(), u => u.GetProperty("email").GetString() == editorA.Email);
    }

    [Fact]
    public async Task Inviting_an_email_that_already_has_an_account_is_a_conflict()
    {
        var adminA = await TestAccounts.CreateAdminAsync(factory);
        var adminB = await TestAccounts.CreateAdminAsync(factory);

        var res = await adminB.Client.PostAsync("/api/v1/users/invite", new { email = adminA.Email, role = "Editor" });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }
}

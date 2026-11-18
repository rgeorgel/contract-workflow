using Contratos.Application.Common;
using Contratos.Infrastructure.Platform;

namespace Contratos.Api;

/// <summary>
/// Contas do portal super admin pela linha de comando (não há cadastro pela tela):
/// <c>dotnet Contratos.Api.dll superadmin list|create|password|deactivate|activate ...</c>.
/// A senha vem de <c>SUPERADMIN_PASSWORD</c> ou é pedida no terminal (sem eco), nunca por argumento.
/// </summary>
internal static class PlatformAdminCli
{
    private const string Usage = """
        Uso:
          superadmin list
          superadmin create <email> <nome>
          superadmin password <email>
          superadmin deactivate <email>
          superadmin activate <email>

        A senha (mínimo de 12 caracteres) vem de SUPERADMIN_PASSWORD ou é pedida no terminal.
        """;

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        await using var scope = services.CreateAsyncScope();
        var admins = scope.ServiceProvider.GetRequiredService<PlatformAdminService>();
        var ct = CancellationToken.None;

        switch (args)
        {
            case ["list"]:
                var all = await admins.ListAsync(ct);
                if (all.Count == 0)
                    Console.WriteLine("Nenhuma conta de super admin.");
                foreach (var a in all)
                    Console.WriteLine($"{a.Email}\t{a.Name}\t{(a.Active ? "ativa" : "desativada")}\túltimo login: {a.LastLoginAt?.ToString("u") ?? "nunca"}");
                return 0;
            case ["create", var email, .. var nameParts] when nameParts.Length > 0:
                return Report(await admins.CreateAsync(email, string.Join(' ', nameParts), ReadPassword(), ct), "Conta criada");
            case ["password", var email]:
                return Report(await admins.SetPasswordAsync(email, ReadPassword(), ct), "Senha trocada (sessões abertas encerradas)");
            case ["deactivate", var email]:
                return Report(await admins.SetActiveAsync(email, false, ct), "Conta desativada (sessões abertas encerradas)");
            case ["activate", var email]:
                return Report(await admins.SetActiveAsync(email, true, ct), "Conta reativada");
            default:
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }

    private static int Report(Result<PlatformAdminDto> result, string done)
    {
        if (result.IsSuccess)
        {
            Console.WriteLine($"{done}: {result.Value!.Email}");
            return 0;
        }
        Console.Error.WriteLine(result.Error!.Title);
        if (result.Error.Fields is { } fields)
            foreach (var messages in fields.ToDictionary().Values)
                foreach (var m in messages)
                    Console.Error.WriteLine($"  {m}");
        return 1;
    }

    private static string ReadPassword()
    {
        if (Environment.GetEnvironmentVariable("SUPERADMIN_PASSWORD") is { Length: > 0 } fromEnv)
            return fromEnv;
        if (Console.IsInputRedirected)
            return Console.ReadLine() ?? "";
        var first = Prompt("Senha: ");
        var second = Prompt("Repita a senha: ");
        if (first != second)
        {
            Console.Error.WriteLine("As senhas não conferem.");
            Environment.Exit(1);
        }
        return first;
    }

    private static string Prompt(string label)
    {
        Console.Write(label);
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (chars.Count > 0) chars.RemoveAt(chars.Count - 1);
                continue;
            }
            if (!char.IsControl(key.KeyChar))
                chars.Add(key.KeyChar);
        }
        Console.WriteLine();
        return new string([.. chars]);
    }
}

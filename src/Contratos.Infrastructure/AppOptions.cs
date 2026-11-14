namespace Contratos.Infrastructure;

public sealed class AppOptions
{
    public const string Section = "App";

    public string ProductName { get; set; } = "Contratos";

    /// <summary>
    /// URL pública usada nos links dos e-mails. Vem de configuração, nunca do cabeçalho Host,
    /// para que um Host forjado não gere links para outro domínio.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:7700";
}

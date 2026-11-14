namespace Contratos.Domain.Users;

public enum UserRole
{
    /// <summary>Gerencia usuários, templates e configurações.</summary>
    Admin,
    /// <summary>Cria e envia contratos.</summary>
    Editor,
    /// <summary>Só consulta.</summary>
    Leitor,
}

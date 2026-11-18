namespace Contratos.Application.Platform;

/// <summary>Conta do portal super admin na requisição atual (cookie próprio). Nulo fora do portal.</summary>
public interface ICurrentPlatformAdmin
{
    Guid? Id { get; }
    string? Name { get; }
    string? Email { get; }
}

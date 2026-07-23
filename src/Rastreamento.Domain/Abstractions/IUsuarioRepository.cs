using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IUsuarioRepository
{
    Task<Usuario?> ObterPorNomeUsuarioAsync(string nomeUsuario, CancellationToken ct);
    Task<Usuario?> ObterPorIdAsync(int id, CancellationToken ct);
}

using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IUsuarioRepository
{
    /// <summary>
    /// Retorna o usuario RASTREADO (sem <c>AsNoTracking</c>), com <c>Perfil</c> carregado. O
    /// rastreamento e requisito do lockout: o caso de uso muta <c>FalhasConsecutivas</c> /
    /// <c>BloqueadoAte</c> na entidade e conta com o change tracking para o
    /// <see cref="SalvarAlteracoesAsync"/> enxergar a mudanca.
    /// </summary>
    Task<Usuario?> ObterPorNomeUsuarioAsync(string nomeUsuario, CancellationToken ct);

    Task SalvarAlteracoesAsync(CancellationToken ct);
}

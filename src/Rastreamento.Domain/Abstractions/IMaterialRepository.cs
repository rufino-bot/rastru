using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IMaterialRepository
{
    /// <summary>
    /// Retorna o material RASTREADO (sem <c>AsNoTracking</c>): <c>Editar</c> e <c>DefinirAtivo</c>
    /// mutam a entidade e contam com o change tracking para o
    /// <see cref="SalvarAlteracoesAsync"/> enxergar a mudanca.
    /// </summary>
    Task<Material?> ObterPorIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// Existe para o caso de uso detectar duplicidade ANTES do insert e devolver erro de negocio,
    /// em vez de deixar a violacao de <c>UQ_Material_Codigo</c> estourar como excecao ate a API.
    /// </summary>
    Task<Material?> ObterPorCodigoAsync(string codigo, CancellationToken ct);

    Task<IReadOnlyList<Material>> ListarAsync(bool incluirInativos, CancellationToken ct);

    Task AdicionarAsync(Material material, CancellationToken ct);

    Task SalvarAlteracoesAsync(CancellationToken ct);
}

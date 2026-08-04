using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface ISetorRepository
{
  /// <summary>
  /// Retorna o setor RASTREADO (sem <c>AsNoTracking</c>): <c>Editar</c> e <c>DefinirAtivo</c>
  /// mutam a entidade e contam com o change tracking para o
  /// <see cref="SalvarAlteracoesAsync"/> enxergar a mudanca.
  /// </summary>
  Task<Setor?> ObterPorIdAsync(int id, CancellationToken ct);

  /// <summary>
  /// Existe para o caso de uso detectar duplicidade ANTES do insert e devolver erro de negocio,
  /// em vez de deixar a violacao de <c>UQ_Setor_Nome</c> estourar como excecao ate a API.
  /// </summary>
  Task<Setor?> ObterPorNomeAsync(string nome, CancellationToken ct);

  Task<IReadOnlyList<Setor>> ListarAsync(bool incluirInativos, CancellationToken ct);

  Task AdicionarAsync(Setor setor, CancellationToken ct);

  Task SalvarAlteracoesAsync(CancellationToken ct);
}

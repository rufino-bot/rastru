using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Acesso as tres tabelas de receita padrao de um Componente.
///
/// UM repositorio para as tres, e nao tres, porque elas tem a MESMA forma de acesso — ler por
/// ComponenteId, substituir por ComponenteId — e porque a deteccao de ciclo precisa caminhar o
/// grafo de filhos por VARIOS componentes, nao so pelo da linha sendo gravada. Num repositorio
/// dedicado a uma tabela, esse caminhamento ficaria separado de onde a regra vive.
/// </summary>
public interface IReceitaPadraoRepository
{
  /// <summary>Null quando nao existe — o caso de uso traduz para 404.</summary>
  Task<Componente?> ObterComponenteAsync(int id, CancellationToken ct);

  Task<IReadOnlyList<ComponenteFilhoPadrao>> ListarFilhosAsync(int componenteId, CancellationToken ct);

  Task<IReadOnlyList<ComponenteMaterialPadrao>> ListarMateriaisAsync(int componenteId, CancellationToken ct);

  /// <summary>Ja vem ordenado por `Ordem`: a tela desenha a sequencia direto.</summary>
  Task<IReadOnlyList<ComponenteRoteiroPadrao>> ListarRoteiroAsync(int componenteId, CancellationToken ct);

  /// <summary>
  /// Os Componentes referenciados pelos ids pedidos. Devolve SO os que existem — o caso de uso
  /// descobre os ausentes pela diferenca, e assim uma consulta responde "existe?" e "esta ativo?"
  /// de uma vez.
  /// </summary>
  Task<IReadOnlyList<Componente>> ObterComponentesPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct);

  Task<IReadOnlyList<Material>> ObterMateriaisPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct);

  Task<IReadOnlyList<Setor>> ObterSetoresPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct);

  /// <summary>
  /// TODAS as arestas pai-filho do catalogo, para a deteccao de ciclo (Task 5).
  ///
  /// Fronteira declarada: isto le a tabela inteira. E aceitavel porque ComponenteFilhoPadrao e
  /// tabela de CATALOGO — cresce com o numero de pecas cadastradas, nao com a producao. Se um dia
  /// doer, a substituicao e um CTE recursivo no banco, nao um cache aqui.
  /// </summary>
  Task<IReadOnlyList<ComponenteFilhoPadrao>> ListarTodasAsArestasAsync(CancellationToken ct);

  /// <summary>
  /// Apaga as linhas do componente e grava as novas num UNICO SaveChanges — ou seja, numa unica
  /// transacao implicita do EF. Meio-termo (apagou e nao gravou) nao e estado alcancavel.
  /// </summary>
  Task SubstituirFilhosAsync(
      int componenteId, IReadOnlyList<ComponenteFilhoPadrao> novas, CancellationToken ct);

  Task SubstituirMateriaisAsync(
      int componenteId, IReadOnlyList<ComponenteMaterialPadrao> novas, CancellationToken ct);

  Task SubstituirRoteiroAsync(
      int componenteId, IReadOnlyList<ComponenteRoteiroPadrao> novas, CancellationToken ct);
}

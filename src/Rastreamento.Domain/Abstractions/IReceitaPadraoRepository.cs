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
  /// A receita do componente passa a ser EXATAMENTE estas linhas: o que estava la e apagado por
  /// PREDICADO (todas as linhas do componente, inclusive as que este chamador nunca leu) e as
  /// novas entram, dentro de uma UNICA transacao explicita. Meio-termo — apagou e nao gravou —
  /// nao e estado alcancavel.
  ///
  /// Sob gravacao simultanea no mesmo componente o resultado e a receita de UM dos escritores,
  /// nunca a uniao das duas; o perdedor e derrubado pelo banco e a implementacao sobe
  /// <see cref="ConflitoDeConcorrenciaException"/> — o mesmo tipo do fluxo de refresh token, para
  /// que o caso de uso a traduza para 409 sem referenciar o EF Core. Erro que NAO e de
  /// concorrencia (violacao de FK, por exemplo) continua subindo cru.
  /// </summary>
  Task SubstituirFilhosAsync(
      int componenteId, IReadOnlyList<ComponenteFilhoPadrao> novas, CancellationToken ct);

  Task SubstituirMateriaisAsync(
      int componenteId, IReadOnlyList<ComponenteMaterialPadrao> novas, CancellationToken ct);

  Task SubstituirRoteiroAsync(
      int componenteId, IReadOnlyList<ComponenteRoteiroPadrao> novas, CancellationToken ct);
}

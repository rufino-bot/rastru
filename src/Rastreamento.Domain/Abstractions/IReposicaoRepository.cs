using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// A reposicao (spec da Fase 5B-2): a reclamacao do cliente registrada no Pedido original e o vinculo do Retrabalho
/// com a baixa ou a reclamacao que ele repoe. Toda leitura devolve dado
/// SOLTO (sem change tracking); a escrita e `Adicionar`, e o repositorio NAO tem `Salvar` proprio — quem persiste e o
/// <see cref="IExecucaoRepository.SalvarAlteracoesAsync"/>, porque o `DbContext` e o mesmo (escopado) e o registro
/// nasce dentro da transacao da execucao. A reclamacao e so de inclusao: a unica escrita depois de nascer e o estorno.
/// </summary>
public interface IReposicaoRepository
{
  void Adicionar(Reclamacao reclamacao);

  Task<Reclamacao?> ObterReclamacaoAsync(int id, CancellationToken ct);

  /// <summary>As reclamacoes do Pedido, mais recente primeiro (`DataHora`, e o `Id` desempatando).</summary>
  Task<IReadOnlyList<Reclamacao>> ListarReclamacoesAsync(int pedidoId, CancellationToken ct);

  /// <summary>A soma das reclamacoes NAO estornadas da Peca.</summary>
  Task<decimal> SomarReclamadoAsync(int pecaId, CancellationToken ct);

  /// <summary>As reclamacoes NAO estornadas da Peca (as que compoem <see cref="SomarReclamadoAsync"/>).</summary>
  Task<IReadOnlyList<Reclamacao>> ListarReclamacoesVivasDaPecaAsync(int pecaId, CancellationToken ct);

  /// <summary>
  /// Marca a reclamacao como estornada, condicionado a `EstornadaEm IS NULL`: e a unica escrita que ela recebe
  /// depois de nascer, e nunca sobrescreve um estorno anterior. Devolve se marcou.
  /// </summary>
  Task<bool> MarcarReclamacaoEstornadaAsync(int id, int usuarioId, DateTime em, CancellationToken ct);

  Task<bool> PedidoExisteAsync(int pedidoId, CancellationToken ct);

  /// <summary>O numero de cada Pedido pedido, por Id. Pedido inexistente nao volta.</summary>
  Task<IReadOnlyDictionary<int, string>> ListarNumerosDePedidosAsync(IReadOnlyCollection<int> pedidoIds, CancellationToken ct);

  /// <summary>
  /// As baixas de perda (`PerdaArmazem` e `MortaEmProcesso`) dos Pedidos que ainda nao tem Retrabalho: o que pode estar
  /// "a repor" (secao 4.1 da spec da Fase 5B-2). O estorno da baixa e a dispensa vigente se leem a parte. Uma consulta
  /// para todos os Pedidos pedidos.
  /// </summary>
  Task<IReadOnlyList<Perda>> ListarPerdasSemRetrabalhoAsync(IReadOnlyCollection<int> pedidoIds, CancellationToken ct);

  /// <summary>As reclamacoes NAO estornadas dos Pedidos que ainda nao tem Retrabalho. Uma consulta para todos os Pedidos.</summary>
  Task<IReadOnlyList<Reclamacao>> ListarReclamacoesSemRetrabalhoAsync(IReadOnlyCollection<int> pedidoIds, CancellationToken ct);

  void Adicionar(DispensaDeReposicao dispensa);

  Task<DispensaDeReposicao?> ObterDispensaAsync(int id, CancellationToken ct);

  /// <summary>
  /// As dispensas VIGENTES (`EstornadaEm IS NULL`) das baixas e das reclamacoes pedidas, em uma consulta: no maximo uma
  /// por alvo (indice unico filtrado).
  /// </summary>
  Task<IReadOnlyList<DispensaDeReposicao>> ListarDispensasVigentesAsync(
      IReadOnlyCollection<int> perdaIds, IReadOnlyCollection<int> reclamacaoIds, CancellationToken ct);

  /// <summary>
  /// Marca a dispensa como desfeita, condicionado a `EstornadaEm IS NULL`: e a unica escrita que ela recebe depois de
  /// nascer, e nunca sobrescreve um estorno anterior. Devolve se marcou.
  /// </summary>
  Task<bool> MarcarDispensaEstornadaAsync(int id, int usuarioId, DateTime em, CancellationToken ct);

  /// <summary>
  /// Grava o Retrabalho que repoe a baixa (C2 da spec da Fase 5B-2), condicionado a `PedidoRetrabalhoId IS NULL`: e
  /// a unica escrita que a `Perda` recebe depois de nascer, e nunca troca um vinculo ja gravado. Devolve se gravou.
  /// </summary>
  Task<bool> MarcarPerdaRepostaAsync(int perdaId, int pedidoRetrabalhoId, CancellationToken ct);

  /// <summary>
  /// Grava o Retrabalho que atende a reclamacao, condicionado a ela nao ter um nem estar estornada. Devolve se gravou.
  /// </summary>
  Task<bool> MarcarReclamacaoRepostaAsync(int reclamacaoId, int pedidoRetrabalhoId, CancellationToken ct);

  /// <summary>
  /// Os Retrabalhos que tem o Pedido como origem, do mais antigo ao mais novo (`DataAbertura`, e o `Id` desempatando),
  /// cada um com a baixa de perda ou a reclamacao que ele repoe. A baixa `Reaproveitado` que aponta o Retrabalho nao
  /// e origem dele: e a sobra que foi para ele.
  /// </summary>
  Task<IReadOnlyList<RetrabalhoDoPedido>> ListarRetrabalhosAsync(int pedidoOrigemId, CancellationToken ct);
}

/// <summary>Um Retrabalho do Pedido de origem e o alvo que ele repoe: a baixa, a reclamacao, ou nenhum (avulso).</summary>
public sealed record RetrabalhoDoPedido(Pedido Pedido, int? PerdaId, int? ReclamacaoId);

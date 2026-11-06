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

  /// <summary>
  /// Marca a reclamacao como estornada, condicionado a `EstornadaEm IS NULL`: e a unica escrita que ela recebe
  /// depois de nascer, e nunca sobrescreve um estorno anterior. Devolve se marcou.
  /// </summary>
  Task<bool> MarcarReclamacaoEstornadaAsync(int id, int usuarioId, DateTime em, CancellationToken ct);

  Task<bool> PedidoExisteAsync(int pedidoId, CancellationToken ct);

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

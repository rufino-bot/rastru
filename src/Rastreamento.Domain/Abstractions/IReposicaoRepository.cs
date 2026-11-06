using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// A reposicao (spec da Fase 5B-2): a reclamacao do cliente registrada no Pedido original. Toda leitura devolve dado
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
}

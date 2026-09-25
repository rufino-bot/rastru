using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Um no com o Pedido e o Agrupamento em que vive — o que fila e tarefas precisam para mostrar o
/// caminho "Pedido > Agrupamento > pai" sem uma consulta por linha.
/// </summary>
public sealed record ContextoDoNo(
    EstruturaItem No, int PedidoId, string PedidoNumero, int AgrupamentoId, string AgrupamentoCodigo);

public sealed record PedidoDoNo(int PedidoId, string Status);

/// <summary>
/// O livro de movimentacoes e o que as escritas da Fase 3 precisam em volta dele (spec da Fase 3,
/// secoes 7 e 8). Toda leitura devolve dado SOLTO (sem change tracking); as escritas sao
/// `Adicionar` + `SalvarAlteracoesAsync`, e as duas unicas atualizacoes (`MarcarPedidoEmProducaoAsync`,
/// `MarcarMontagemEstornadaAsync`) sao conjuntistas — nenhuma toca `Movimentacao`, que e so de inclusao.
/// </summary>
public interface IExecucaoRepository
{
  /// <summary>
  /// Roda `trabalho` numa transacao SERIALIZABLE e commita. Deadlock e lock timeout (1205/1222) sobem
  /// como <see cref="ConflitoDeConcorrenciaException"/>. O `trabalho` so escreve depois de validar
  /// tudo: um `Result` de falha devolvido de dentro dele commita uma transacao sem escrita nenhuma.
  /// </summary>
  Task<T> EmTransacaoAsync<T>(Func<Task<T>> trabalho, CancellationToken ct);

  /// <summary>
  /// Trava (UPDLOCK, HOLDLOCK) as linhas de `EstruturaItem`, UMA A UMA, em ordem crescente de Id, e as
  /// devolve. Id inexistente simplesmente nao volta. So vale dentro de <see cref="EmTransacaoAsync"/>:
  /// fora dela a trava acabaria no fim do SELECT, e o metodo lanca.
  /// </summary>
  Task<IReadOnlyList<EstruturaItem>> TravarNosAsync(IEnumerable<int> ids, CancellationToken ct);

  Task<IReadOnlyList<EstruturaItem>> ListarNosAsync(IReadOnlyCollection<int> ids, CancellationToken ct);

  Task<IReadOnlyList<EstruturaItem>> ListarFilhosAsync(int paiId, CancellationToken ct);

  /// <summary>O proprio no e todos os descendentes; vazio se o no nao existe.</summary>
  Task<IReadOnlyList<int>> ListarIdsDaSubarvoreAsync(int id, CancellationToken ct);

  /// <summary>Todo no de Pedido que nao esta `Concluido` nem `Cancelado` — o escopo de fila e tarefas.</summary>
  Task<IReadOnlyList<ContextoDoNo>> ListarNosEmProducaoAsync(CancellationToken ct);

  Task<PedidoDoNo?> ObterPedidoDoNoAsync(int estruturaItemId, CancellationToken ct);

  /// <summary>`Aberto` passa a `EmProducao`; qualquer outro status fica como esta (regra 28).</summary>
  Task MarcarPedidoEmProducaoAsync(int pedidoId, CancellationToken ct);

  /// <summary>Saldo liquido por no e posicao (spec secao 7.1), a mesma conta de `Livro.SomarSaldos`.</summary>
  Task<IReadOnlyList<SaldoLiquido>> ListarSaldosAsync(IReadOnlyCollection<int> ids, CancellationToken ct);

  /// <summary>Soma das montagens NAO estornadas, por no pai. No sem montagem nao aparece.</summary>
  Task<IReadOnlyDictionary<int, decimal>> ListarTotaisMontadosAsync(IReadOnlyCollection<int> ids, CancellationToken ct);

  /// <summary>Toda `Ordem` que aparece como origem ou destino no livro do no (spec secao 4.6).</summary>
  Task<IReadOnlyList<(int EstruturaItemId, int Ordem)>> ListarPassosAlcancadosAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct);

  Task<Movimentacao?> ObterMovimentacaoAsync(int id, CancellationToken ct);

  Task<IReadOnlyList<Movimentacao>> ListarMovimentacoesDoNoAsync(int estruturaItemId, CancellationToken ct);

  /// <summary>Dos movimentos pedidos, os que ja tem um Estorno apontando para eles.</summary>
  Task<IReadOnlySet<int>> ListarEstornadasAsync(IReadOnlyCollection<int> movimentacaoIds, CancellationToken ct);

  Task<Montagem?> ObterMontagemAsync(int id, CancellationToken ct);

  Task<IReadOnlyList<Montagem>> ListarMontagensDoNoAsync(int paiId, CancellationToken ct);

  /// <summary>As baixas de filho (Tipo = Montagem) das montagens pedidas — sem os estornos delas.</summary>
  Task<IReadOnlyList<Movimentacao>> ListarBaixasAsync(IReadOnlyCollection<int> montagemIds, CancellationToken ct);

  Task MarcarMontagemEstornadaAsync(int montagemId, int usuarioId, DateTime em, CancellationToken ct);

  Task<IReadOnlyDictionary<int, string>> ListarNomesDeUsuariosAsync(IReadOnlyCollection<int> ids, CancellationToken ct);

  /// <summary>
  /// Apaga os passos do Roteiro do no com `Ordem` maior que `ultimaOrdemTravada` (todos, se nula) e
  /// grava `novos`. Passo alcancado e historico e nao sai (spec secao 4.6).
  /// </summary>
  Task SubstituirPassosNaoAlcancadosAsync(
      int estruturaItemId, int? ultimaOrdemTravada, IReadOnlyList<(int SetorId, int Ordem)> novos, CancellationToken ct);

  void Adicionar(Movimentacao movimentacao);

  void Adicionar(Montagem montagem);

  Task SalvarAlteracoesAsync(CancellationToken ct);
}

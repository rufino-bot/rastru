using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Um no com o Pedido e o Agrupamento em que vive — o que fila e tarefas precisam para mostrar o
/// caminho "Pedido > Agrupamento > pai" sem uma consulta por linha. `Materiais` sao os do NO
/// (`EstruturaMaterial`), por codigo — nunca os do catalogo do Componente.
/// </summary>
public sealed record ContextoDoNo(
    EstruturaItem No, int PedidoId, string PedidoNumero, string PedidoCliente, int AgrupamentoId,
    string AgrupamentoCodigo, PausaAberta? Pausa, IReadOnlyList<MaterialDoNo> Materiais);

/// <summary>Um material gravado no no: o que o filtro da fila e das tarefas oferece e casa.</summary>
public sealed record MaterialDoNo(int Id, string Codigo, string Descricao);

/// <summary>A pausa aberta de um Pedido, com o nome de quem pausou (spec da Fase 3D, secao 2.5).</summary>
public sealed record PausaAberta(int PedidoId, DateTime PausadoEm, int PausadoPorUsuarioId, string PausadoPorNome, string? Motivo);

/// <summary>`Pausado`: o Pedido tem pausa aberta — o Iniciar a recusa (spec da Fase 3D, secao 4.1).</summary>
public sealed record PedidoDoNo(int PedidoId, string Numero, string Status, bool Pausado);

public sealed record PedidoTravado(int Id, string Numero, string Status);

/// <summary>A materia-prima do estorno rapido da fila (spec da Fase 3D, secao 2.4).</summary>
public sealed record RegistrosDoSetor(IReadOnlyList<Movimentacao> Movimentos, IReadOnlyList<Montagem> Montagens);

/// <summary>
/// O livro de movimentacoes e o que as escritas da Fase 3 precisam em volta dele (spec da Fase 3,
/// secoes 7 e 8). Toda leitura devolve dado SOLTO (sem change tracking); as escritas sao
/// `Adicionar` + `SalvarAlteracoesAsync`, e as tres unicas atualizacoes (`MarcarPedidoEmProducaoAsync`,
/// `MarcarMontagemEstornadaAsync`, `FecharPausaAsync`) sao conjuntistas — nenhuma toca `Movimentacao`,
/// que e so de inclusao.
/// </summary>
public interface IExecucaoRepository
{
  /// <summary>
  /// Roda `trabalho` numa transacao SERIALIZABLE e commita. Deadlock (1205) tenta de novo, com
  /// transacao nova a cada vez (3 tentativas no total: a inicial mais 2 retentativas em producao);
  /// esgotadas as tentativas, ou num lock timeout (1222, que nunca tenta de novo), sobe
  /// <see cref="ConflitoDeConcorrenciaException"/>. O `trabalho` so escreve depois de validar
  /// tudo: um `Result` de falha devolvido de dentro dele commita uma transacao sem escrita nenhuma.
  /// </summary>
  Task<T> EmTransacaoAsync<T>(Func<Task<T>> trabalho, CancellationToken ct);

  /// <summary>
  /// Roda `leitura` SEM transacao explicita (fica no READ COMMITTED da conexao), com o mesmo retry de
  /// deadlock (1205) de <see cref="EmTransacaoAsync"/>: uma leitura tambem pode ser escolhida vitima
  /// contra um escritor Serializable. Esgotadas as tentativas, ou num lock timeout (1222), sobe
  /// <see cref="ConflitoDeConcorrenciaException"/> — o caso de uso traduz para 409, como nas escritas.
  /// </summary>
  Task<T> LerAsync<T>(Func<Task<T>> leitura, CancellationToken ct);

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

  /// <summary>
  /// Igual a <see cref="ObterPedidoDoNoAsync"/>, mas trava a linha do Pedido com UPDLOCK: uso exclusivo
  /// do caminho que VAI escrever nela a seguir (hoje, so <c>ApontamentoUseCase.Iniciar</c>, antes de
  /// <see cref="MarcarPedidoEmProducaoAsync"/>). Ver o XML doc de <c>ExecucaoRepository</c> para o
  /// motivo: sem isto, duas transacoes que leem o mesmo Pedido com S e depois tentam converter para X
  /// deadlockam (spec 8.1; achado de review da Task 11 com deadlock graph do
  /// <c>system_health</c> — PK_Pedido).
  /// </summary>
  Task<PedidoDoNo?> ObterPedidoDoNoParaEscritaAsync(int estruturaItemId, CancellationToken ct);

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

  /// <summary>
  /// O `Inicio` do pai que esta montagem gravou (spec da Fase 3D, secao 3.3), ou nulo — montagem
  /// gravada antes da Fase 3D nao tem. `UX_Movimentacao_UmInicioPorMontagem` garante no maximo um.
  /// </summary>
  Task<Movimentacao?> ObterInicioDaMontagemAsync(int montagemId, CancellationToken ct);

  Task MarcarMontagemEstornadaAsync(int montagemId, int usuarioId, DateTime em, CancellationToken ct);

  Task<IReadOnlyDictionary<int, string>> ListarNomesDeUsuariosAsync(IReadOnlyCollection<int> ids, CancellationToken ct);

  /// <summary>
  /// Dos nos pedidos: os `Inicio` (sem Montagem — o inicio de um pai se estorna pela montagem) e os
  /// `Termino` com destino neste Setor que ainda nao foram estornados, e as montagens feitas neste
  /// Setor que ainda valem. Se cabe no saldo e quem pode ver, decide o caso de uso.
  /// </summary>
  Task<RegistrosDoSetor> ListarRegistrosEstornaveisDoSetorAsync(int setorId, IReadOnlyCollection<int> ids, CancellationToken ct);

  /// <summary>
  /// Apaga os passos do Roteiro do no com `Ordem` maior que `ultimaOrdemTravada` (todos, se nula) e
  /// grava `novos`. Passo alcancado e historico e nao sai (spec secao 4.6).
  /// </summary>
  Task SubstituirPassosNaoAlcancadosAsync(
      int estruturaItemId, int? ultimaOrdemTravada, IReadOnlyList<(int SetorId, int Ordem)> novos, CancellationToken ct);

  /// <summary>
  /// Trava a linha do Pedido com UPDLOCK — a mesma que o Iniciar trava por
  /// <see cref="ObterPedidoDoNoParaEscritaAsync"/> —, para pausar e iniciar se serializarem (spec da
  /// Fase 3D, secao 4.5). So vale dentro de <see cref="EmTransacaoAsync"/>.
  /// </summary>
  Task<PedidoTravado?> TravarPedidoAsync(int pedidoId, CancellationToken ct);

  Task<PedidoPausa?> ObterPausaAbertaAsync(int pedidoId, CancellationToken ct);

  /// <summary>Fecha o intervalo: conjuntista e condicionado a `RetomadoEm IS NULL`, nunca sobrescreve.</summary>
  Task FecharPausaAsync(int pausaId, int usuarioId, DateTime em, CancellationToken ct);

  void Adicionar(Movimentacao movimentacao);

  void Adicionar(Montagem montagem);

  void Adicionar(PedidoPausa pausa);

  Task SalvarAlteracoesAsync(CancellationToken ct);
}

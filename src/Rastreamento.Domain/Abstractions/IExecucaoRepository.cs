using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Um no com o Pedido e o Agrupamento em que vive — o que fila e tarefas precisam para mostrar o
/// caminho "Pedido > Agrupamento > pai" sem uma consulta por linha. `Materiais` sao os do NO
/// (`EstruturaMaterial`), por codigo — nunca os do catalogo do Componente. `PedidoDataEntrega` e
/// `PedidoStatus` vao no fim, com valor padrao, para a lista do Separador decidir o atraso sem uma
/// consulta por Pedido (decisao P3 do plano de backend da Fase 4).
/// </summary>
public sealed record ContextoDoNo(
    EstruturaItem No, int PedidoId, string PedidoNumero, string PedidoCliente, int AgrupamentoId,
    string AgrupamentoCodigo, string AgrupamentoTipo, PausaAberta? Pausa, IReadOnlyList<MaterialDoNo> Materiais,
    DateOnly PedidoDataEntrega = default, string PedidoStatus = "Aberto");

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
/// O status e o fechamento de um Pedido como a regra 13 os calcula (C11, C12 e C13 da spec da Fase 5A), para
/// o repositorio gravar. `PedidoConcluido` e sempre `Status == "Concluido"`. `Agrupamentos` traz so os que tem
/// Peca (C16): o que nao aparece aqui nao e tocado.
/// </summary>
public sealed record FechamentoCalculado(
    int PedidoId, string Status, bool PedidoConcluido, IReadOnlyList<AgrupamentoNoFechamento> Agrupamentos);

/// <summary>Um Agrupamento com Peca no <see cref="FechamentoCalculado"/>: concluido quando todas as Pecas dele concluem.</summary>
public sealed record AgrupamentoNoFechamento(int AgrupamentoId, bool Concluido);

/// <summary>
/// Uma Peca (no raiz) de um Pedido, com o Agrupamento em que vive e a quantidade total dela: o que o fechamento
/// (C11, C12 e C16 da spec da Fase 5A) precisa para decidir o status do Pedido e a conclusao dos Agrupamentos.
/// </summary>
public sealed record PecaDoPedido(int PedidoId, int AgrupamentoId, int PecaId, decimal Quantidade);

/// <summary>
/// O livro de movimentacoes e o que as escritas da Fase 3 precisam em volta dele (spec da Fase 3,
/// secoes 7 e 8). Toda leitura devolve dado SOLTO (sem change tracking); as escritas sao
/// `Adicionar` + `SalvarAlteracoesAsync`, e as unicas atualizacoes (`MarcarPedidoEmProducaoAsync`,
/// `MarcarMontagemEstornadaAsync`, `FecharPausaAsync`, `GravarFechamentoAsync`) sao conjuntistas — nenhuma toca `Movimentacao`,
/// que e so de inclusao.
/// </summary>
public interface IExecucaoRepository
{
  /// <summary>
  /// Roda `trabalho` numa transacao SERIALIZABLE e commita. Deadlock (1205) tenta de novo, com
  /// transacao nova a cada vez (3 tentativas no total: a inicial mais 2 retentativas em producao);
  /// esgotadas as tentativas, ou num lock timeout (1222, que nunca tenta de novo), sobe
  /// <see cref="ConflitoDeConcorrenciaException"/>. Esta forma commita sempre que o `trabalho` volta sem
  /// lancar; quem devolve um `Result` de falha depois de ter escrito usa a sobrecarga com `confirmar`.
  /// </summary>
  Task<T> EmTransacaoAsync<T>(Func<Task<T>> trabalho, CancellationToken ct);

  /// <summary>
  /// Como <see cref="EmTransacaoAsync{T}(Func{Task{T}}, CancellationToken)"/>, mas so commita se
  /// `confirmar(resultado)` for verdadeiro; senao desfaz a transacao, limpa o que o `trabalho` deixou no
  /// change tracker e devolve o resultado mesmo assim. Existe porque o lote (iniciar ou terminar varios
  /// nos de uma vez) grava o item 1 antes de validar o item 2: uma falha devolvida no meio nao pode
  /// deixar o primeiro no livro. Um `trabalho` que lanca continua desfazendo, como sempre.
  /// </summary>
  Task<T> EmTransacaoAsync<T>(Func<Task<T>> trabalho, Func<T, bool> confirmar, CancellationToken ct);

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
  /// do caminho que VAI escrever nela a seguir (hoje, so o nucleo do iniciar de <c>ApontamentoUseCase</c>,
  /// individual ou em lote, antes de <see cref="MarcarPedidoEmProducaoAsync"/>). Ver o XML doc de
  /// <c>ExecucaoRepository</c> para o motivo: sem isto, duas transacoes que leem o mesmo Pedido com S e
  /// depois tentam converter para X deadlockam (spec 8.1; achado de review da Task 11 com deadlock graph
  /// do <c>system_health</c> — PK_Pedido).
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

  /// <summary>Dos nos pedidos, os que vivem num Agrupamento `Kit` (regra 25). No inexistente nao volta.</summary>
  Task<IReadOnlySet<int>> ListarNosDeKitAsync(IReadOnlyCollection<int> ids, CancellationToken ct);

  /// <summary>
  /// Todo Setor com `UtilizaKit`, inativos inclusive: o que ja aguarda montagem num Setor inativado continua
  /// contando nos conjuntos a espera (spec da Fase 3B, D5).
  /// </summary>
  Task<IReadOnlySet<int>> ListarSetoresComKitAsync(CancellationToken ct);

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

  /// <summary>
  /// Trava, como <see cref="TravarPedidoAsync"/>, a linha de cada Pedido em que vivem os nos pedidos —
  /// UMA A UMA, em ordem crescente de Id — e devolve os PedidoIds distintos, nessa ordem. O PedidoId sai
  /// da juncao `EstruturaItem` x `Agrupamento`, sem ler a linha do Pedido antes do UPDLOCK. No inexistente
  /// nao contribui. Existe para o iniciar em lote travar todos os Pedidos antes do primeiro item, depois
  /// de todos os nos (spec da Fase 3, secao 8.1: ordem fixa de aquisicao). So vale dentro de
  /// <see cref="EmTransacaoAsync"/>: fora dela, lanca.
  /// </summary>
  Task<IReadOnlyList<int>> TravarPedidosDosNosAsync(IReadOnlyCollection<int> estruturaItemIds, CancellationToken ct);

  Task<PedidoPausa?> ObterPausaAbertaAsync(int pedidoId, CancellationToken ct);

  /// <summary>Fecha o intervalo: conjuntista e condicionado a `RetomadoEm IS NULL`, nunca sobrescreve.</summary>
  Task FecharPausaAsync(int pausaId, int usuarioId, DateTime em, CancellationToken ct);

  /// <summary>
  /// As Pecas (nos raiz, `EstruturaPaiId` nulo) dos Agrupamentos dos Pedidos pedidos. Agrupamento sem Peca nao
  /// aparece (C16: nao conclui e nao segura o Pedido); Item nunca aparece. Pedido sem Peca simplesmente nao volta.
  /// </summary>
  Task<IReadOnlyList<PecaDoPedido>> ListarPecasDosPedidosAsync(IReadOnlyCollection<int> pedidoIds, CancellationToken ct);

  /// <summary>
  /// Grava o <see cref="FechamentoCalculado"/>: o `Status` do Pedido e a `DataConclusao` dele e dos Agrupamentos
  /// que a lista traz (e so eles). Conjuntista, como as demais atualizacoes: `DataConclusao` vira
  /// `COALESCE(DataConclusao, agora)` quando concluido — uma data ja gravada nao e sobrescrita — e `NULL` quando
  /// nao (o estorno de remessa a limpa, C13). Dentro da transacao do movimento que mudou o fechamento.
  /// </summary>
  Task GravarFechamentoAsync(FechamentoCalculado fechamento, DateTime agora, CancellationToken ct);

  void Adicionar(Movimentacao movimentacao);

  void Adicionar(Montagem montagem);

  void Adicionar(PedidoPausa pausa);

  Task SalvarAlteracoesAsync(CancellationToken ct);
}

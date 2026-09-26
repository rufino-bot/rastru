using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Testa `ExecucaoRepository.EmTransacaoAsync` DIRETAMENTE (sem passar por nenhum caso de uso),
/// reproduzindo o padrao vulneravel que motivou o retry (Task 11, fix round 1): uma leitura
/// SERIALIZABLE comum (S) seguida de um UPDATE condicional no MESMO recurso — exatamente o que
/// `ApontamentoUseCase.Iniciar` fazia antes do UPDLOCK (`ObterPedidoDoNoAsync` +
/// `MarcarPedidoEmProducaoAsync`). Nao usa `ApontamentoUseCase`/`ArvoreDeTesteNoBanco`: prova o
/// RETRY em isolamento, sobre um Pedido solto, sem depender de Peca/Roteiro/Setor nenhum.
///
/// <para>
/// As duas transacoes fazem RENDEZVOUS (cada lado sinaliza a propria leitura e espera o outro
/// sinalizar a dele antes de tentar o UPDATE): sem isto, a corrida entre duas conexoes separadas
/// nao garante que as duas cheguem a fase de conversao S -&gt; X ao mesmo tempo, e o teste ficaria
/// flaky por depender do agendamento do SO. Com o rendezvous, as duas SEMPRE tentam converter
/// simultaneamente — e por isso o teste de 1 tentativa ainda assim repete
/// (<see cref="TentativasParaReproduzir"/> vezes, Pedido novo a cada vez): o SQL Server escolhe o
/// deadlock em cima de um relogio proprio (o monitor de deadlock, nao instantaneo), e a maquina de
/// CI pode variar.
/// </para>
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class RetryDeDeadlockEmTransacaoAsyncTests : TesteComBanco
{
  /// <summary>
  /// Cap explicito, pedido pelo controlador: se o deadlock nao aparecer em nenhuma destas tentativas
  /// (cada uma com um Pedido NOVO), o teste falha dizendo isso — nunca afirma sucesso silenciando a
  /// auséncia de reproducao.
  /// </summary>
  private const int TentativasParaReproduzir = 5;

  [Fact]
  public async Task Com_1_tentativa_pelo_menos_um_lado_sobe_ConflitoDeConcorrencia()
  {
    var pedidosCriados = new List<int>();
    try
    {
      for (var tentativa = 1; tentativa <= TentativasParaReproduzir; tentativa++)
      {
        var pedidoId = await NovoPedidoAbertoAsync();
        pedidosCriados.Add(pedidoId);

        var (falhouA, falhouB) = await CorrerOsDoisLadosAsync(pedidoId, tentativasMaximas: 1);

        if (falhouA || falhouB) return;   // reproduziu o deadlock — o teste passou
      }
      Assert.Fail(
          $"Nao reproduziu o deadlock em {TentativasParaReproduzir} tentativas (Pedido novo em cada "
          + "uma, com 1 tentativa/sem retry) — o padrao vulneravel pode ter deixado de deadlockar de "
          + "verdade nesta maquina, ou o rendezvous nao esta forcando a conversao S -> X simultanea "
          + "como deveria. Nao afirmar sucesso sem a reproducao: reveja o rendezvous antes de "
          + "aumentar o cap.");
    }
    finally
    {
      await LimparPedidosAsync(pedidosCriados);
    }
  }

  [Fact]
  public async Task Com_3_tentativas_os_dois_lados_completam()
  {
    var pedidoId = await NovoPedidoAbertoAsync();
    try
    {
      var (falhouA, falhouB) = await CorrerOsDoisLadosAsync(pedidoId, tentativasMaximas: 3);

      Assert.False(falhouA, "lado A nao deveria falhar com 3 tentativas — o retry deveria absorver o deadlock.");
      Assert.False(falhouB, "lado B nao deveria falhar com 3 tentativas — o retry deveria absorver o deadlock.");
    }
    finally
    {
      await LimparPedidosAsync([pedidoId]);
    }
  }

  /// <summary>
  /// Os dois lados leem o MESMO Pedido com uma consulta SERIALIZABLE comum (sem UPDLOCK — o padrao
  /// vulneravel), fazem rendezvous, e so ENTAO tentam a mesma conversao para X
  /// (`UPDATE ... WHERE Status = 'Aberto'`). Devolve, por lado, se ele subiu
  /// `ConflitoDeConcorrenciaException`.
  /// </summary>
  private static async Task<(bool FalhouA, bool FalhouB)> CorrerOsDoisLadosAsync(int pedidoId, int tentativasMaximas)
  {
    var leuA = new TaskCompletionSource();
    var leuB = new TaskCompletionSource();

    async Task<bool> LadoAsync(TaskCompletionSource euLi, Task esperarOOutroLer)
    {
      await using var contexto = NovoContexto();
      var repo = new ExecucaoRepository(contexto, tentativasMaximas);
      try
      {
        await repo.EmTransacaoAsync(async () =>
        {
          // O padrao vulneravel: leitura SERIALIZABLE comum (S), sem UPDLOCK — igual ao que
          // `ObterPedidoDoNoAsync` fazia antes do fix 1 da Task 11 (fix round 1).
          await contexto.Pedidos.AsNoTracking().Where(p => p.Id == pedidoId)
              .Select(p => p.Status).SingleAsync();
          euLi.TrySetResult();
          await esperarOOutroLer;
          // A conversao S -> X: os dois competem por ela ao mesmo tempo, forcado pelo rendezvous.
          await contexto.Pedidos.Where(p => p.Id == pedidoId && p.Status == "Aberto")
              .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "EmProducao"));
          return 0;
        }, CancellationToken.None);
        return false;
      }
      catch (ConflitoDeConcorrenciaException)
      {
        return true;
      }
    }

    var tarefaA = LadoAsync(leuA, leuB.Task);
    var tarefaB = LadoAsync(leuB, leuA.Task);
    var resultados = await Task.WhenAll(tarefaA, tarefaB);
    return (resultados[0], resultados[1]);
  }

  private static async Task<int> NovoPedidoAbertoAsync()
  {
    await using var db = NovoContexto();
    var autor = await db.Usuarios.AsNoTracking().SingleAsync(u => u.NomeUsuario == "admin");
    var pedido = new Pedido
    {
      Numero = $"retry-{Guid.NewGuid():N}"[..25], Cliente = "Cliente de teste", Tipo = "Fabricacao",
      Status = "Aberto", DataAbertura = DateTime.UtcNow, CriadoPorUsuarioId = autor.Id,
    };
    db.Pedidos.Add(pedido);
    await db.SaveChangesAsync();
    return pedido.Id;
  }

  private static async Task LimparPedidosAsync(IReadOnlyList<int> pedidoIds)
  {
    await using var db = NovoContexto();
    foreach (var id in pedidoIds)
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Pedido WHERE Id = {id}");
  }
}

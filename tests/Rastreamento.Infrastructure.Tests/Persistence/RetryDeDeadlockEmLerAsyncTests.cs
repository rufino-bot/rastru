using System.Data;
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Testa `ExecucaoRepository.LerAsync` DIRETAMENTE (fix round 4 da Task 11): uma LEITURA escolhida
/// vitima de um deadlock real (1205) tenta de novo com o mesmo laco de `EmTransacaoAsync`, e ao
/// esgotar as tentativas sobe `ConflitoDeConcorrenciaException`.
///
/// <para>
/// O que o teste NAO reproduz, e por que: a vitima medida na suite (`GET .../posicoes`) era uma
/// leitura de UM comando em READ COMMITTED, que segura trava so durante o proprio SELECT — o ciclo
/// depende da ordem em que o plano de execucao pega as travas, e nao ha como forcar isso de forma
/// deterministica. Aqui o delegado da leitura abre, ELE MESMO, uma transacao REPEATABLE READ so de
/// SELECTs, para segurar o S da primeira linha enquanto pede a segunda — o `LerAsync` continua sem
/// transacao propria; o que se prova e o comportamento dele diante de um 1205 de verdade vindo de
/// dentro do delegado, que e o mesmo qualquer que seja a forma do ciclo.
/// </para>
///
/// <para>
/// Determinismo: rendezvous igual ao de `RetryDeDeadlockEmTransacaoAsyncTests` (cada lado sinaliza
/// a primeira trava e espera a do outro antes de pedir a segunda), entao o ciclo sempre fecha; e o
/// lado da leitura roda com `DEADLOCK_PRIORITY LOW`, entao a vitima e SEMPRE a leitura — sem isto o
/// SQL Server escolheria pelo custo de desfazer, e o escritor poderia ser o derrubado.
/// </para>
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class RetryDeDeadlockEmLerAsyncTests : TesteComBanco
{
  [Fact]
  public async Task Com_1_tentativa_a_leitura_vitima_sobe_ConflitoDeConcorrencia()
  {
    var (p1, p2) = (await NovoPedidoAbertoAsync(), await NovoPedidoAbertoAsync());
    try
    {
      var (leituraFalhou, invocacoesDaLeitura, escritaFalhou) = await CorrerAsync(p1, p2, tentativasDaLeitura: 1);

      Assert.True(leituraFalhou, "a leitura (DEADLOCK_PRIORITY LOW) deveria ser a vitima e, sem retry, subir ConflitoDeConcorrencia.");
      Assert.Equal(1, invocacoesDaLeitura);
      Assert.False(escritaFalhou, "a escrita nunca deveria ser a vitima.");
    }
    finally
    {
      await LimparPedidosAsync([p1, p2]);
    }
  }

  [Fact]
  public async Task Com_3_tentativas_a_leitura_vitima_tenta_de_novo_e_completa()
  {
    var (p1, p2) = (await NovoPedidoAbertoAsync(), await NovoPedidoAbertoAsync());
    try
    {
      var (leituraFalhou, invocacoesDaLeitura, escritaFalhou) = await CorrerAsync(p1, p2, tentativasDaLeitura: 3);

      Assert.False(leituraFalhou, "com 3 tentativas o retry de LerAsync deveria absorver o deadlock.");
      // 2, e nao 1: prova que houve deadlock E retry (a primeira invocacao foi a vitima).
      Assert.Equal(2, invocacoesDaLeitura);
      Assert.False(escritaFalhou, "a escrita nunca deveria ser a vitima.");
    }
    finally
    {
      await LimparPedidosAsync([p1, p2]);
    }
  }

  /// <summary>
  /// Leitura (via `LerAsync`): S em p1, rendezvous, S em p2. Escrita (via `EmTransacaoAsync`): X em
  /// p2, rendezvous, X em p1. Ciclo garantido; vitima garantida (a leitura, LOW).
  /// </summary>
  private static async Task<(bool LeituraFalhou, int InvocacoesDaLeitura, bool EscritaFalhou)> CorrerAsync(
      int p1, int p2, int tentativasDaLeitura)
  {
    var leituraTravou = new TaskCompletionSource();
    var escritaTravou = new TaskCompletionSource();
    var invocacoes = 0;

    async Task<bool> LeituraAsync()
    {
      await using var contexto = NovoContexto();
      await contexto.Database.OpenConnectionAsync();
      var repo = new ExecucaoRepository(contexto, tentativasDaLeitura);
      try
      {
        await contexto.Database.ExecuteSqlRawAsync("SET DEADLOCK_PRIORITY LOW;");
        await repo.LerAsync(async () =>
        {
          invocacoes++;
          await using var tx = await contexto.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
          await StatusAsync(contexto, p1);
          leituraTravou.TrySetResult();
          await Esperar(escritaTravou.Task);
          await StatusAsync(contexto, p2);
          await tx.CommitAsync();
          return 0;
        }, CancellationToken.None);
        return false;
      }
      catch (ConflitoDeConcorrenciaException)
      {
        return true;
      }
      finally
      {
        await contexto.Database.ExecuteSqlRawAsync("SET DEADLOCK_PRIORITY NORMAL;");
      }
    }

    async Task<bool> EscritaAsync()
    {
      await using var contexto = NovoContexto();
      var repo = new ExecucaoRepository(contexto, tentativasMaximas: 1);
      try
      {
        await repo.EmTransacaoAsync(async () =>
        {
          await MarcarAsync(contexto, p2);
          escritaTravou.TrySetResult();
          await Esperar(leituraTravou.Task);
          await MarcarAsync(contexto, p1);
          return 0;
        }, CancellationToken.None);
        return false;
      }
      catch (ConflitoDeConcorrenciaException)
      {
        return true;
      }
    }

    var resultados = await Task.WhenAll(LeituraAsync(), EscritaAsync());
    return (resultados[0], invocacoes, resultados[1]);
  }

  private static Task<string> StatusAsync(RastreamentoDbContext contexto, int pedidoId) =>
      contexto.Pedidos.AsNoTracking().Where(p => p.Id == pedidoId).Select(p => p.Status).SingleAsync();

  private static Task<int> MarcarAsync(RastreamentoDbContext contexto, int pedidoId) =>
      contexto.Pedidos.Where(p => p.Id == pedidoId)
          .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "EmProducao"));

  /// <summary>Mesmo teto do rendezvous de `RetryDeDeadlockEmTransacaoAsyncTests`: nunca trava a suite.</summary>
  private static async Task Esperar(Task sinalDoOutro)
  {
    if (await Task.WhenAny(sinalDoOutro, Task.Delay(TimeSpan.FromSeconds(30))) != sinalDoOutro)
      throw new TimeoutException("O outro lado do rendezvous nao sinalizou a propria trava em 30s.");
  }

  private static async Task<int> NovoPedidoAbertoAsync()
  {
    await using var db = NovoContexto();
    var autor = await db.Usuarios.AsNoTracking().SingleAsync(u => u.NomeUsuario == "admin");
    var pedido = new Pedido
    {
      Numero = $"ler-{Guid.NewGuid():N}"[..25], Cliente = "Cliente de teste", Tipo = "Fabricacao",
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

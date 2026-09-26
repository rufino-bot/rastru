using Microsoft.EntityFrameworkCore;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// `ExecucaoRepository` contra o SQL Server real (spec da Fase 3, secao 9.2): a soma do livro em SQL
/// bate com a de C#, a trava de linha segura a segunda transacao e vira conflito, e as leituras de
/// apoio devolvem o que prometem. Toda asercao escopada nos nos do proprio cenario.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class ExecucaoRepositoryTests : TesteComBanco
{
  private sealed record Cenario(ArvoreDeTesteNoBanco Arvore, int Peca, int ItemA, int ItemB, int Corte, int Solda)
  {
    public int[] Nos => [Peca, ItemA, ItemB];
  }

  private async Task NoCenarioAsync(Func<Cenario, Task> corpo)
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "exec");
    try
    {
      var corte = await arvore.NovoSetorAsync(db);
      var solda = await arvore.NovoSetorAsync(db);
      var peca = await arvore.NovaPecaAsync(db, 10m);
      var a = await arvore.NovoItemAsync(db, peca, 20m, 2m);
      var b = await arvore.NovoItemAsync(db, peca, 10m, 1m);
      await arvore.RoteiroAsync(db, peca, solda);
      await arvore.RoteiroAsync(db, a, corte);
      await arvore.RoteiroAsync(db, b, corte);
      await corpo(new Cenario(arvore, peca, a, b, corte, solda));
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }

  private static Movimentacao Mov(
      Cenario c, int item, string tipo, Local de, Local para, decimal quantidade,
      int? montagemId = null, int? estornoDeId = null) => new()
  {
    EstruturaItemId = item, Tipo = tipo, Quantidade = quantidade,
    OrigemPosicao = de.Posicao, OrigemSetorId = de.SetorId, OrigemOrdem = de.Ordem,
    DestinoPosicao = para.Posicao, DestinoSetorId = para.SetorId, DestinoOrdem = para.Ordem,
    MontagemId = montagemId, EstornoDeId = estornoDeId, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private async Task<int> GravarAsync(Movimentacao m)
  {
    await using var db = NovoContexto();
    db.Movimentacoes.Add(m);
    await db.SaveChangesAsync();
    return m.Id;
  }

  private async Task<int> GravarAsync(Montagem m)
  {
    await using var db = NovoContexto();
    db.Montagens.Add(m);
    await db.SaveChangesAsync();
    return m.Id;
  }

  /// <summary>Iniciar, terminar, entregar, estornar e montar A e B sob a Peca — todo tipo de movimento.</summary>
  private async Task<(int EntregaEstornada, int Montagem)> PovoarAsync(Cenario c)
  {
    var corte1 = Local.NoSetor(c.Corte, 1);
    var coleta1 = Local.AguardandoColeta(c.Corte, 1);
    var naSolda = Local.AguardandoMontagem(c.Solda);

    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Inicio, Local.AIniciar, corte1, 20m));
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Termino, corte1, coleta1, 12m));
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Entrega, coleta1, naSolda, 10m));

    await GravarAsync(Mov(c, c.ItemB, TiposDeMovimentacao.Inicio, Local.AIniciar, corte1, 10m));
    await GravarAsync(Mov(c, c.ItemB, TiposDeMovimentacao.Termino, corte1, coleta1, 10m));
    var entregaB = await GravarAsync(Mov(c, c.ItemB, TiposDeMovimentacao.Entrega, coleta1, naSolda, 10m));
    await GravarAsync(Mov(c, c.ItemB, TiposDeMovimentacao.Estorno, naSolda, coleta1, 10m, estornoDeId: entregaB));
    await GravarAsync(Mov(c, c.ItemB, TiposDeMovimentacao.Entrega, coleta1, naSolda, 6m));

    var montagem = await GravarAsync(new Montagem
    {
      EstruturaItemId = c.Peca, SetorId = c.Solda, Quantidade = 3m, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    });
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Montagem, naSolda, Local.Montado, 6m, montagemId: montagem));
    await GravarAsync(Mov(c, c.ItemB, TiposDeMovimentacao.Montagem, naSolda, Local.Montado, 3m, montagemId: montagem));

    await GravarAsync(Mov(c, c.Peca, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(c.Solda, 1), 4m));
    return (entregaB, montagem);
  }

  [Fact]
  public Task Saldos_do_banco_batem_com_a_soma_em_CSharp() => NoCenarioAsync(async c =>
  {
    await PovoarAsync(c);

    await using var db = NovoContexto();
    var doBanco = await new ExecucaoRepository(db).ListarSaldosAsync(c.Nos, CancellationToken.None);
    var movimentos = await db.Movimentacoes.AsNoTracking().Where(m => c.Nos.Contains(m.EstruturaItemId)).ToListAsync();
    var emCSharp = Livro.SomarSaldos(movimentos);

    Assert.Equal(emCSharp, doBanco);
    Assert.Contains(new SaldoLiquido(c.ItemA, Posicoes.AguardandoMontagem, c.Solda, null, 4m), doBanco);   // 10 - 6
    Assert.Contains(new SaldoLiquido(c.ItemB, Posicoes.AguardandoMontagem, c.Solda, null, 3m), doBanco);   // 6 - 3
    Assert.Contains(new SaldoLiquido(c.ItemB, Posicoes.AguardandoColeta, c.Corte, 1, 4m), doBanco);        // 10 - 10 + 10 - 6
  });

  [Fact]
  public Task Estornadas_baixas_e_livro_do_no_voltam_escopados() => NoCenarioAsync(async c =>
  {
    var (entregaEstornada, montagem) = await PovoarAsync(c);

    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);
    var livroDeB = await repo.ListarMovimentacoesDoNoAsync(c.ItemB, CancellationToken.None);
    var estornadas = await repo.ListarEstornadasAsync(livroDeB.Select(m => m.Id).ToList(), CancellationToken.None);
    var baixas = await repo.ListarBaixasAsync([montagem], CancellationToken.None);

    Assert.Equal(6, livroDeB.Count);
    Assert.True(livroDeB.Select(m => m.Id).SequenceEqual(livroDeB.Select(m => m.Id).Order()));
    Assert.Equal(new[] { entregaEstornada }, estornadas.ToArray());
    Assert.Equal(new[] { c.ItemA, c.ItemB }, baixas.Select(b => b.EstruturaItemId).ToArray());
  });

  [Fact]
  public Task Totais_montados_ignoram_montagem_estornada() => NoCenarioAsync(async c =>
  {
    await GravarAsync(new Montagem
    {
      EstruturaItemId = c.Peca, SetorId = c.Solda, Quantidade = 3m, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    });
    await GravarAsync(new Montagem
    {
      EstruturaItemId = c.Peca, SetorId = c.Solda, Quantidade = 2m, DataHora = DateTime.UtcNow.AddMinutes(-5),
      UsuarioId = c.Arvore.AutorId, EstornadaEm = DateTime.UtcNow, EstornadaPorUsuarioId = c.Arvore.AutorId,
    });

    await using var db = NovoContexto();
    var totais = await new ExecucaoRepository(db).ListarTotaisMontadosAsync(c.Nos, CancellationToken.None);

    Assert.Equal(3m, totais[c.Peca]);
    Assert.False(totais.ContainsKey(c.ItemA));
  });

  [Fact]
  public Task Passos_alcancados_juntam_origem_e_destino_sem_repetir() => NoCenarioAsync(async c =>
  {
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(c.Corte, 1), 5m));
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Termino,
        Local.NoSetor(c.Corte, 1), Local.AguardandoColeta(c.Corte, 1), 5m));

    await using var db = NovoContexto();
    var passos = await new ExecucaoRepository(db).ListarPassosAlcancadosAsync(c.Nos, CancellationToken.None);

    Assert.Equal(new[] { (c.ItemA, 1) }, passos);
  });

  [Fact]
  public Task Trava_segura_a_segunda_transacao_e_o_timeout_vira_conflito() => NoCenarioAsync(async c =>
  {
    await using var dbA = NovoContexto();
    await using var dbB = NovoContexto();
    var repoA = new ExecucaoRepository(dbA);
    var repoB = new ExecucaoRepository(dbB);
    // Conexao de B aberta a mao, para o SET valer na MESMA conexao que a transacao dela vai usar.
    await dbB.Database.OpenConnectionAsync();
    await dbB.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 300");

    await repoA.EmTransacaoAsync(async () =>
    {
      await repoA.TravarNosAsync([c.Peca], CancellationToken.None);
      await Assert.ThrowsAsync<ConflitoDeConcorrenciaException>(() => repoB.EmTransacaoAsync(async () =>
      {
        await repoB.TravarNosAsync([c.Peca], CancellationToken.None);
        return 0;
      }, CancellationToken.None));
      return 0;
    }, CancellationToken.None);
  });

  [Fact]
  public Task Travar_fora_de_transacao_e_erro_de_programacao() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => new ExecucaoRepository(db).TravarNosAsync([c.Peca], CancellationToken.None));
  });

  [Fact]
  public Task Travar_devolve_so_os_nos_que_existem() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);
    var travados = await repo.EmTransacaoAsync(
        () => repo.TravarNosAsync([c.ItemB, int.MaxValue, c.Peca], CancellationToken.None), CancellationToken.None);

    Assert.Equal(new[] { c.Peca, c.ItemB }, travados.Select(n => n.Id).ToArray());
  });

  [Fact]
  public Task Pedido_Aberto_passa_a_EmProducao_e_outro_status_nao_muda() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);

    await repo.MarcarPedidoEmProducaoAsync(c.Arvore.PedidoId, CancellationToken.None);
    Assert.Equal(new PedidoDoNo(c.Arvore.PedidoId, "EmProducao"), await repo.ObterPedidoDoNoAsync(c.ItemA, CancellationToken.None));

    await db.Pedidos.Where(p => p.Id == c.Arvore.PedidoId)
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Concluido"));
    await repo.MarcarPedidoEmProducaoAsync(c.Arvore.PedidoId, CancellationToken.None);
    Assert.Equal("Concluido", (await repo.ObterPedidoDoNoAsync(c.ItemA, CancellationToken.None))!.Status);
  });

  [Fact]
  public Task Nos_em_producao_deixam_de_fora_Pedido_cancelado() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);

    var antes = (await repo.ListarNosEmProducaoAsync(CancellationToken.None)).Where(x => c.Nos.Contains(x.No.Id)).ToList();
    await db.Pedidos.Where(p => p.Id == c.Arvore.PedidoId)
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Cancelado"));
    var depois = (await repo.ListarNosEmProducaoAsync(CancellationToken.None)).Where(x => c.Nos.Contains(x.No.Id)).ToList();

    Assert.Equal(3, antes.Count);
    Assert.All(antes, x => Assert.Equal("AG-01", x.AgrupamentoCodigo));
    Assert.Empty(depois);
  });

  [Fact]
  public Task Subarvore_traz_o_no_e_os_descendentes() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var ids = await new ExecucaoRepository(db).ListarIdsDaSubarvoreAsync(c.Peca, CancellationToken.None);

    Assert.Equal(c.Nos.Order().ToArray(), ids.Order().ToArray());
  });

  [Fact]
  public Task Substituir_passos_preserva_os_travados_e_grava_os_novos() => NoCenarioAsync(async c =>
  {
    await using var escrita = NovoContexto();
    var itemC = await c.Arvore.NovoItemAsync(escrita, c.Peca, 10m, 1m);
    await c.Arvore.RoteiroAsync(escrita, itemC, c.Corte, c.Solda, c.Corte);

    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);
    await repo.EmTransacaoAsync(async () =>
    {
      await repo.SubstituirPassosNaoAlcancadosAsync(itemC, 1, [(c.Solda, 2)], CancellationToken.None);
      return 0;
    }, CancellationToken.None);

    await using var leitura = NovoContexto();
    var roteiro = await leitura.EstruturaRoteiros.AsNoTracking()
        .Where(r => r.EstruturaItemId == itemC).OrderBy(r => r.Ordem).Select(r => new { r.SetorId, r.Ordem }).ToListAsync();
    Assert.Equal(new[] { (c.Corte, 1), (c.Solda, 2) }, roteiro.Select(r => (r.SetorId, r.Ordem)).ToArray());
  });
}

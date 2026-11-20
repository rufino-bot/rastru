using Microsoft.EntityFrameworkCore;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// As quatro leituras em lote que alimentam a idade da Fase 6A (livro dos nos, criacao dos Agrupamentos, pausas e nos
/// do Pedido), contra o SQL Server real. Toda asercao escopada nos Ids da arvore do proprio teste.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class LeiturasDaIdadeTests : TesteComBanco
{
  private async Task NaArvoreAsync(Func<ArvoreDeTesteNoBanco, Task> corpo)
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "idade");
    try
    {
      await corpo(arvore);
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }

  private async Task<int> GravarAsync(Movimentacao m)
  {
    await using var db = NovoContexto();
    db.Movimentacoes.Add(m);
    await db.SaveChangesAsync();
    return m.Id;
  }

  private static Movimentacao Linha(
      ArvoreDeTesteNoBanco a, int no, string tipo, Local de, Local para, decimal quantidade, int? estornoDeId = null) => new()
  {
    EstruturaItemId = no, Tipo = tipo, Quantidade = quantidade,
    OrigemPosicao = de.Posicao, OrigemSetorId = de.SetorId, OrigemOrdem = de.Ordem,
    DestinoPosicao = para.Posicao, DestinoSetorId = para.SetorId, DestinoOrdem = para.Ordem,
    EstornoDeId = estornoDeId, DataHora = DateTime.UtcNow, UsuarioId = a.AutorId,
  };

  [Fact]
  public Task Movimentacoes_em_lote_trazem_os_estornos() => NaArvoreAsync(async a =>
  {
    int peca, item, deOutro;
    int setor;
    await using (var db = NovoContexto())
    {
      setor = await a.NovoSetorAsync(db);
      peca = await a.NovaPecaAsync(db, 10m);
      item = await a.NovoItemAsync(db, peca, 20m, 2m);
      deOutro = await a.NovaPecaAsync(db, 5m);
    }
    var inicio = await GravarAsync(Linha(a, item, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(setor, 1), 8m));
    var estorno = await GravarAsync(
        Linha(a, item, TiposDeMovimentacao.Estorno, Local.NoSetor(setor, 1), Local.AIniciar, 8m, estornoDeId: inicio));
    var daPeca = await GravarAsync(Linha(a, peca, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(setor, 1), 2m));
    await GravarAsync(Linha(a, deOutro, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(setor, 1), 1m));

    await using var leitura = NovoContexto();
    var linhas = await new ExecucaoRepository(leitura).ListarMovimentacoesAsync([item, peca], CancellationToken.None);

    Assert.Equal([inicio, estorno, daPeca], linhas.Select(l => l.Id).ToList());
    Assert.Equal(inicio, linhas.Single(l => l.Id == estorno).EstornoDeId);
    Assert.Empty(await new ExecucaoRepository(leitura).ListarMovimentacoesAsync([], CancellationToken.None));
  });

  [Fact]
  public Task Linhas_ate_um_id_param_no_id_e_filtram_os_nos() => NaArvoreAsync(async a =>
  {
    int noA, noB, noC, setor;
    await using (var db = NovoContexto())
    {
      setor = await a.NovoSetorAsync(db);
      noA = await a.NovaPecaAsync(db, 10m);
      noB = await a.NovaPecaAsync(db, 10m);
      noC = await a.NovaPecaAsync(db, 10m);
    }
    var l1 = await GravarAsync(Linha(a, noA, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(setor, 1), 1m));
    var l2 = await GravarAsync(Linha(a, noB, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(setor, 1), 2m));
    var l3 = await GravarAsync(Linha(a, noA, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(setor, 1), 3m));
    await GravarAsync(Linha(a, noC, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(setor, 1), 4m));

    await using var leitura = NovoContexto();
    var linhas = await new ExecucaoRepository(leitura).ListarMovimentacoesAteAsync([noA, noB], l2, CancellationToken.None);

    Assert.Equal([l1, l2], linhas.Select(l => l.Id).ToList());
    Assert.DoesNotContain(l3, linhas.Select(l => l.Id));
    Assert.Empty(await new ExecucaoRepository(leitura).ListarMovimentacoesAteAsync([], l3, CancellationToken.None));
  });

  [Fact]
  public Task Intervalo_de_ids_traz_qualquer_no_e_respeita_os_dois_limites() => NaArvoreAsync(async a =>
  {
    int noA, noB, noC, noD, setor;
    await using (var db = NovoContexto())
    {
      setor = await a.NovoSetorAsync(db);
      noA = await a.NovaPecaAsync(db, 10m);
      noB = await a.NovaPecaAsync(db, 10m);
      noC = await a.NovaPecaAsync(db, 10m);
      noD = await a.NovaPecaAsync(db, 10m);
    }
    var l1 = await GravarAsync(Linha(a, noA, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(setor, 1), 1m));
    var l2 = await GravarAsync(Linha(a, noB, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(setor, 1), 1m));
    var l3 = await GravarAsync(Linha(a, noC, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(setor, 1), 1m));
    var l4 = await GravarAsync(Linha(a, noD, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(setor, 1), 1m));

    await using var leitura = NovoContexto();
    var linhas = await new ExecucaoRepository(leitura).ListarMovimentacoesDoIntervaloAsync(l1, l3, CancellationToken.None);

    var doTeste = new[] { l1, l2, l3, l4 };
    Assert.Equal([l2, l3], linhas.Select(l => l.Id).Where(doTeste.Contains).ToList());
    Assert.Equal(linhas.Select(l => l.Id).OrderBy(i => i).ToList(), linhas.Select(l => l.Id).ToList());
  });

  [Fact]
  public Task Criacao_dos_agrupamentos() => NaArvoreAsync(async a =>
  {
    await using var db = NovoContexto();
    var gravado = (await db.Agrupamentos.AsNoTracking().SingleAsync(x => x.Id == a.AgrupamentoId)).CriadoEm;

    var criacao = await new ExecucaoRepository(db).ListarCriacaoDosAgrupamentosAsync(
        [a.AgrupamentoId, int.MaxValue], CancellationToken.None);

    Assert.Equal(gravado, criacao[a.AgrupamentoId]);
    Assert.False(criacao.ContainsKey(int.MaxValue));
    Assert.Empty(await new ExecucaoRepository(db).ListarCriacaoDosAgrupamentosAsync([], CancellationToken.None));
  });

  [Fact]
  public Task Pausas_do_pedido_em_ordem() => NaArvoreAsync(async a =>
  {
    var t0 = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
    await using (var db = NovoContexto())
    {
      // Gravadas fora de ordem: a leitura ordena por PausadoEm, nao por Id.
      db.PedidoPausas.Add(new PedidoPausa { PedidoId = a.PedidoId, PausadoEm = t0.AddHours(5), PausadoPorUsuarioId = a.AutorId });
      db.PedidoPausas.Add(new PedidoPausa
      {
        PedidoId = a.PedidoId, PausadoEm = t0, PausadoPorUsuarioId = a.AutorId,
        RetomadoEm = t0.AddHours(2), RetomadoPorUsuarioId = a.AutorId, Motivo = "falta de material",
      });
      await db.SaveChangesAsync();
    }

    await using var leitura = NovoContexto();
    var pausas = await new ExecucaoRepository(leitura).ListarPausasDoPedidoAsync(a.PedidoId, CancellationToken.None);

    Assert.Equal([t0, t0.AddHours(5)], pausas.Select(p => p.PausadoEm).ToList());
    Assert.NotNull(pausas[0].RetomadoEm);
    Assert.Null(pausas[1].RetomadoEm);
    Assert.Empty(await new ExecucaoRepository(leitura).ListarPausasDoPedidoAsync(int.MaxValue, CancellationToken.None));
  });

  [Fact]
  public Task Nos_do_pedido_incluem_pedido_concluido() => NaArvoreAsync(async a =>
  {
    int peca, item;
    await using (var db = NovoContexto())
    {
      peca = await a.NovaPecaAsync(db, 3m);
      item = await a.NovoItemAsync(db, peca, 6m, 2m);
      await db.Pedidos.Where(p => p.Id == a.PedidoId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Concluido"));
    }

    await using var leitura = NovoContexto();
    var repo = new ExecucaoRepository(leitura);
    var nos = await repo.ListarNosDoPedidoAsync(a.PedidoId, CancellationToken.None);

    Assert.Equal([peca, item], nos.Select(n => n.No.Id).ToList());
    Assert.All(nos, n =>
    {
      Assert.Equal(a.PedidoId, n.PedidoId);
      Assert.Equal("Concluido", n.PedidoStatus);
      Assert.Equal(a.AgrupamentoId, n.AgrupamentoId);
    });
    // A leitura da fila continua sem os Pedidos concluidos.
    var emProducao = await repo.ListarNosEmProducaoAsync(false, CancellationToken.None);
    Assert.DoesNotContain(emProducao, n => n.PedidoId == a.PedidoId);
    var comConcluidos = await repo.ListarNosEmProducaoAsync(true, CancellationToken.None);
    Assert.Contains(comConcluidos, n => n.No.Id == peca);
  });
}

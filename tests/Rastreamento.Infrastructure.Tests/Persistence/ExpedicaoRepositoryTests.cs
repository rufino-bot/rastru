using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// <see cref="ExpedicaoRepository"/> e os dois metodos do fechamento do <see cref="ExecucaoRepository"/> contra o
/// SQL Server real (spec da Fase 5A, secoes 3.4 a 3.7). Cada teste cria a SUA arvore (prefixo unico) e afirma so
/// sobre os Ids dela. Mesma <c>[Collection]</c> do mapeamento do livro, porque a arvore escreve em `Componente`.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class ExpedicaoRepositoryTests : TesteComBanco
{
  private sealed record Cenario(ArvoreDeTesteNoBanco Arvore, int PecaId, int OutraPecaId, int SetorId);

  private async Task NoCenarioAsync(Func<Cenario, Task> corpo)
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "exprepo");
    try
    {
      var setor = await arvore.NovoSetorAsync(db);
      var peca = await arvore.NovaPecaAsync(db, 10m);
      var outra = await arvore.NovaPecaAsync(db, 5m);
      await corpo(new Cenario(arvore, peca, outra, setor));
    }
    finally { await arvore.LimparAsync(NovoContexto); }
  }

  private async Task<T> GravarAsync<T>(T linha) where T : class
  {
    await using var db = NovoContexto();
    db.Add(linha);
    await db.SaveChangesAsync();
    return linha;
  }

  private static RelatorioDimensional Afericao(Cenario c, int pecaId, string resultado = ResultadosDeAfericao.NaoConforme) => new()
  {
    EstruturaItemId = pecaId, Tipo = TiposDeAfericao.Afericao, Resultado = resultado,
    DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private static RelatorioDimensional EstornoDeAfericao(Cenario c, RelatorioDimensional original) => new()
  {
    EstruturaItemId = original.EstruturaItemId, Tipo = TiposDeAfericao.Estorno, EstornoDeId = original.Id,
    DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private static LiberacaoDimensional PedidoDeLiberacao(ArvoreDeTesteNoBanco arvore, int pecaId) => new()
  {
    EstruturaItemId = pecaId, Tipo = TiposDeLiberacao.Pedido, Quantidade = 2m, Texto = "Cliente aceita",
    DataHora = DateTime.UtcNow, UsuarioId = arvore.AutorId,
  };

  private static Movimentacao Linha(
      ArvoreDeTesteNoBanco arvore, int pecaId, string tipo, string de, string para, decimal quantidade = 1m) => new()
  {
    EstruturaItemId = pecaId, Tipo = tipo, Quantidade = quantidade,
    OrigemPosicao = de, DestinoPosicao = para, DataHora = DateTime.UtcNow, UsuarioId = arvore.AutorId,
  };

  private static async Task<Pedido> LerPedidoAsync(RastreamentoDbContext db, int id) =>
      await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == id);

  [Fact]
  public Task Listar_afericoes_traz_as_da_peca_com_os_estornos() => NoCenarioAsync(async c =>
  {
    var primeira = await GravarAsync(Afericao(c, c.PecaId));
    var estorno = await GravarAsync(EstornoDeAfericao(c, primeira));
    var segunda = await GravarAsync(Afericao(c, c.PecaId, ResultadosDeAfericao.Conforme));
    var daOutra = await GravarAsync(Afericao(c, c.OutraPecaId));

    await using var db = NovoContexto();
    var repo = new ExpedicaoRepository(db);

    var daPeca = await repo.ListarAfericoesAsync([c.PecaId], CancellationToken.None);
    var ambas = await repo.ListarAfericoesAsync([c.PecaId, c.OutraPecaId], CancellationToken.None);
    var nenhuma = await repo.ListarAfericoesAsync([], CancellationToken.None);

    Assert.Equal([primeira.Id, estorno.Id, segunda.Id], daPeca.Select(a => a.Id));
    Assert.Equal(TiposDeAfericao.Estorno, daPeca[1].Tipo);
    Assert.Equal(primeira.Id, daPeca[1].EstornoDeId);
    Assert.Equal([primeira.Id, estorno.Id, segunda.Id, daOutra.Id], ambas.Select(a => a.Id));
    Assert.Empty(nenhuma);

    var obtida = await repo.ObterAfericaoAsync(segunda.Id, CancellationToken.None);
    Assert.Equal(ResultadosDeAfericao.Conforme, obtida!.Resultado);
    Assert.Null(await repo.ObterAfericaoAsync(int.MaxValue, CancellationToken.None));
  });

  [Fact]
  public Task Afericoes_estornadas_sao_as_com_estorno_apontando() => NoCenarioAsync(async c =>
  {
    var estornada = await GravarAsync(Afericao(c, c.PecaId));
    var valida = await GravarAsync(Afericao(c, c.PecaId));
    var estorno = await GravarAsync(EstornoDeAfericao(c, estornada));

    await using var db = NovoContexto();
    var repo = new ExpedicaoRepository(db);

    var estornadas = await repo.ListarAfericoesEstornadasAsync(
        [estornada.Id, valida.Id, estorno.Id], CancellationToken.None);
    var seSoPerguntarPelaValida = await repo.ListarAfericoesEstornadasAsync([valida.Id], CancellationToken.None);
    var nenhuma = await repo.ListarAfericoesEstornadasAsync([], CancellationToken.None);

    // So a original: o estorno em si nao tem estorno apontando, e a valida tambem nao.
    Assert.Equal(new HashSet<int> { estornada.Id }, estornadas.ToHashSet());
    Assert.Empty(seSoPerguntarPelaValida);
    Assert.Empty(nenhuma);
  });

  [Fact]
  public Task Linhas_da_contagem_incluem_o_estorno_dela() => NoCenarioAsync(async c =>
  {
    var contagem = await GravarAsync(new ContagemDimensional
    {
      EstruturaItemId = c.PecaId, Conformes = 4m, NaoConformes = 2m, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    });
    var outraContagem = await GravarAsync(new ContagemDimensional
    {
      EstruturaItemId = c.PecaId, Conformes = 1m, NaoConformes = 1m, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    });

    var conformes = Linha(c.Arvore, c.PecaId, TiposDeMovimentacao.Medicao, Posicoes.NaExpedicao, Posicoes.Conforme, 4m);
    var naoConformes = Linha(c.Arvore, c.PecaId, TiposDeMovimentacao.Medicao, Posicoes.NaExpedicao, Posicoes.NaoConforme, 2m);
    conformes.ContagemId = naoConformes.ContagemId = contagem.Id;
    await GravarAsync(conformes);
    await GravarAsync(naoConformes);

    // O estorno da contagem e uma linha inversa que aponta a original e carrega o MESMO ContagemId.
    var estorno = Linha(c.Arvore, c.PecaId, TiposDeMovimentacao.Estorno, Posicoes.Conforme, Posicoes.NaExpedicao, 4m);
    estorno.ContagemId = contagem.Id;
    estorno.EstornoDeId = conformes.Id;
    await GravarAsync(estorno);

    var daOutra = Linha(c.Arvore, c.PecaId, TiposDeMovimentacao.Medicao, Posicoes.NaExpedicao, Posicoes.Conforme, 1m);
    daOutra.ContagemId = outraContagem.Id;
    await GravarAsync(daOutra);

    await using var db = NovoContexto();
    var repo = new ExpedicaoRepository(db);

    var linhas = await repo.ListarLinhasDasContagensAsync([contagem.Id], CancellationToken.None);
    var nenhuma = await repo.ListarLinhasDasContagensAsync([], CancellationToken.None);

    Assert.Equal([conformes.Id, naoConformes.Id, estorno.Id], linhas.Select(m => m.Id));
    Assert.All(linhas, m => Assert.Equal(contagem.Id, m.ContagemId));
    Assert.Equal(conformes.Id, linhas.Single(m => m.Tipo == TiposDeMovimentacao.Estorno).EstornoDeId);
    Assert.Empty(nenhuma);

    var obtida = await repo.ObterContagemAsync(contagem.Id, CancellationToken.None);
    Assert.Equal((4m, 2m), (obtida!.Conformes, obtida.NaoConformes));
    var contagens = await repo.ListarContagensAsync([c.PecaId], CancellationToken.None);
    Assert.Equal([contagem.Id, outraContagem.Id], contagens.Select(x => x.Id));
  });

  [Fact]
  public Task Linhas_da_liberacao_e_da_remessa_trazem_so_as_que_apontam_o_registro() => NoCenarioAsync(async c =>
  {
    var pedido = await GravarAsync(PedidoDeLiberacao(c.Arvore, c.PecaId));
    var outroPedido = await GravarAsync(PedidoDeLiberacao(c.Arvore, c.PecaId));
    var daLiberacao = Linha(c.Arvore, c.PecaId, TiposDeMovimentacao.PedidoDeLiberacao, Posicoes.NaoConforme, Posicoes.AguardandoLiberacao);
    daLiberacao.LiberacaoId = pedido.Id;
    var daOutra = Linha(c.Arvore, c.PecaId, TiposDeMovimentacao.PedidoDeLiberacao, Posicoes.NaoConforme, Posicoes.AguardandoLiberacao);
    daOutra.LiberacaoId = outroPedido.Id;
    await GravarAsync(daLiberacao);
    await GravarAsync(daOutra);

    var remessa = await GravarAsync(new Remessa { DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId });
    c.Arvore.RemessaIds.Add(remessa.Id);
    var daRemessa = Linha(c.Arvore, c.PecaId, TiposDeMovimentacao.Expedicao, Posicoes.NaExpedicao, Posicoes.Expedido, 3m);
    daRemessa.RemessaId = remessa.Id;
    await GravarAsync(daRemessa);

    await using var db = NovoContexto();
    var repo = new ExpedicaoRepository(db);

    var daLib = await repo.ListarLinhasDasLiberacoesAsync([pedido.Id], CancellationToken.None);
    var daRem = await repo.ListarLinhasDasRemessasAsync([remessa.Id], CancellationToken.None);

    Assert.Equal(daLiberacao.Id, Assert.Single(daLib).Id);
    Assert.Equal(daRemessa.Id, Assert.Single(daRem).Id);
    Assert.Empty(await repo.ListarLinhasDasLiberacoesAsync([], CancellationToken.None));
    Assert.Empty(await repo.ListarLinhasDasRemessasAsync([], CancellationToken.None));
    Assert.Equal(2m, (await repo.ObterLiberacaoAsync(pedido.Id, CancellationToken.None))!.Quantidade);
    Assert.Equal(c.Arvore.AutorId, (await repo.ObterRemessaAsync(remessa.Id, CancellationToken.None))!.UsuarioId);
    var liberacoes = await repo.ListarLiberacoesAsync([c.PecaId], CancellationToken.None);
    Assert.Equal([pedido.Id, outroPedido.Id], liberacoes.Select(l => l.Id));
  });

  [Fact]
  public Task Pedidos_de_liberacao_nao_traz_resposta_nem_Pedido_concluido() => NoCenarioAsync(async c =>
  {
    var pedidoAberto = await GravarAsync(PedidoDeLiberacao(c.Arvore, c.PecaId));
    var resposta = await GravarAsync(new LiberacaoDimensional
    {
      EstruturaItemId = c.PecaId, Tipo = TiposDeLiberacao.Resposta, PedidoDeLiberacaoId = pedidoAberto.Id,
      Quantidade = 1m, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    });

    // Um Pedido encerrado e um cancelado, cada um com a Peca e o pedido de liberacao dele.
    await using var db = NovoContexto();
    var concluida = await ArvoreDeTesteNoBanco.CriarAsync(db, "exprepoc");
    var cancelada = await ArvoreDeTesteNoBanco.CriarAsync(db, "exprepox");
    try
    {
      var pecaConcluida = await concluida.NovaPecaAsync(db, 3m);
      var pecaCancelada = await cancelada.NovaPecaAsync(db, 3m);
      await db.Pedidos.Where(p => p.Id == concluida.PedidoId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Concluido"));
      await db.Pedidos.Where(p => p.Id == cancelada.PedidoId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Cancelado"));
      var doConcluido = await GravarAsync(PedidoDeLiberacao(concluida, pecaConcluida));
      var doCancelado = await GravarAsync(PedidoDeLiberacao(cancelada, pecaCancelada));

      await using var leitura = NovoContexto();
      var todos = await new ExpedicaoRepository(leitura).ListarPedidosDeLiberacaoAsync(CancellationToken.None);

      // Escopado nas tres arvores do teste: a tabela e compartilhada.
      var meus = todos.Where(l => new[] { c.PecaId, pecaConcluida, pecaCancelada }.Contains(l.EstruturaItemId)).ToList();
      Assert.Equal(pedidoAberto.Id, Assert.Single(meus).Id);
      Assert.DoesNotContain(todos, l => l.Id == resposta.Id);
      Assert.DoesNotContain(todos, l => l.Id == doConcluido.Id || l.Id == doCancelado.Id);
    }
    finally
    {
      await concluida.LimparAsync(NovoContexto);
      await cancelada.LimparAsync(NovoContexto);
    }
  });

  [Fact]
  public Task Remessas_desde_respeita_a_data() => NoCenarioAsync(async c =>
  {
    var agora = DateTime.UtcNow;
    var antiga = await GravarAsync(new Remessa { DataHora = agora.AddDays(-31), UsuarioId = c.Arvore.AutorId });
    var recente = await GravarAsync(new Remessa { DataHora = agora.AddDays(-1), UsuarioId = c.Arvore.AutorId });
    var deAgora = await GravarAsync(new Remessa { DataHora = agora, UsuarioId = c.Arvore.AutorId });
    c.Arvore.RemessaIds.AddRange([antiga.Id, recente.Id, deAgora.Id]);

    await using var db = NovoContexto();
    var remessas = await new ExpedicaoRepository(db).ListarRemessasDesdeAsync(agora.AddDays(-30), CancellationToken.None);

    // Escopado nas tres: outras remessas do banco podem aparecer na janela.
    var minhas = remessas.Where(r => r.Id == antiga.Id || r.Id == recente.Id || r.Id == deAgora.Id).ToList();
    Assert.Equal([deAgora.Id, recente.Id], minhas.Select(r => r.Id));   // mais recente primeiro; a de 31 dias nao volta
  });

  [Fact]
  public Task Pecas_dos_pedidos_traz_so_as_raizes_com_Agrupamento() => NoCenarioAsync(async c =>
  {
    var item = await NovoItemAsync(c);
    int vazioId;
    await using (var escrita = NovoContexto())
    {
      // Um Agrupamento sem Peca no mesmo Pedido: ele nao pode aparecer (C16).
      var vazio = new Agrupamento
      {
        PedidoId = c.Arvore.PedidoId, Codigo = "AG-VAZIO", Tipo = "Avulso",
        CriadoPorUsuarioId = c.Arvore.AutorId, CriadoEm = DateTime.UtcNow,
      };
      escrita.Agrupamentos.Add(vazio);
      await escrita.SaveChangesAsync();
      vazioId = vazio.Id;
    }
    await using var db = NovoContexto();
    var outra = await ArvoreDeTesteNoBanco.CriarAsync(db, "exprepop");
    try
    {
      var pecaDoOutro = await outra.NovaPecaAsync(db, 7m);
      var repo = new ExecucaoRepository(db);

      var pecas = await repo.ListarPecasDosPedidosAsync([c.Arvore.PedidoId], CancellationToken.None);
      var dosDois = await repo.ListarPecasDosPedidosAsync([c.Arvore.PedidoId, outra.PedidoId], CancellationToken.None);
      var nenhum = await repo.ListarPecasDosPedidosAsync([], CancellationToken.None);

      Assert.Equal(
          [new PecaDoPedido(c.Arvore.PedidoId, c.Arvore.AgrupamentoId, c.PecaId, 10m),
           new PecaDoPedido(c.Arvore.PedidoId, c.Arvore.AgrupamentoId, c.OutraPecaId, 5m)],
          pecas);
      Assert.DoesNotContain(pecas, p => p.PecaId == item);          // Item nunca
      Assert.DoesNotContain(pecas, p => p.AgrupamentoId == vazioId); // Agrupamento sem Peca nunca
      Assert.Equal(3, dosDois.Count);
      Assert.Equal(new PecaDoPedido(outra.PedidoId, outra.AgrupamentoId, pecaDoOutro, 7m),
          Assert.Single(dosDois, p => p.PedidoId == outra.PedidoId));
      Assert.Empty(nenhum);
    }
    finally
    {
      await outra.LimparAsync(NovoContexto);
      await using var limpeza = NovoContexto();
      await limpeza.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Agrupamento WHERE Id = {vazioId}");
    }
  });

  private async Task<int> NovoItemAsync(Cenario c)
  {
    await using var db = NovoContexto();
    return await c.Arvore.NovoItemAsync(db, c.PecaId, 20m, 2m);
  }

  [Fact]
  public Task Gravar_fechamento_preserva_a_DataConclusao_ja_gravada() => NoCenarioAsync(async c =>
  {
    // Depois da abertura do Pedido da arvore (`CK_Pedido_ConclusaoAposAbertura`).
    var t1 = DateTime.UtcNow.AddHours(1);
    var t2 = t1.AddDays(3);
    var pedidoId = c.Arvore.PedidoId;

    async Task GravarAsync(bool concluido, DateTime agora)
    {
      await using var db = NovoContexto();
      await new ExecucaoRepository(db).GravarFechamentoAsync(
          new FechamentoCalculado(pedidoId, concluido ? "Concluido" : "AguardandoExpedicao", concluido,
              [new AgrupamentoNoFechamento(c.Arvore.AgrupamentoId, concluido)]),
          agora, CancellationToken.None);
    }

    async Task<(Pedido Pedido, Agrupamento Agrupamento)> LerAsync()
    {
      await using var db = NovoContexto();
      return (await LerPedidoAsync(db, pedidoId),
          await db.Agrupamentos.AsNoTracking().SingleAsync(a => a.Id == c.Arvore.AgrupamentoId));
    }

    await GravarAsync(concluido: true, t1);
    var primeira = await LerAsync();
    Assert.Equal("Concluido", primeira.Pedido.Status);
    Assert.Equal(t1, primeira.Pedido.DataConclusao!.Value, TimeSpan.FromSeconds(1));
    Assert.Equal(t1, primeira.Agrupamento.DataConclusao!.Value, TimeSpan.FromSeconds(1));

    // Gravar de novo concluido, mais tarde: a data ja gravada fica (COALESCE).
    await GravarAsync(concluido: true, t2);
    var segunda = await LerAsync();
    Assert.Equal(t1, segunda.Pedido.DataConclusao!.Value, TimeSpan.FromSeconds(1));
    Assert.Equal(t1, segunda.Agrupamento.DataConclusao!.Value, TimeSpan.FromSeconds(1));

    // Nao concluido: o status vai e a data e limpa (o estorno de remessa, C13).
    await GravarAsync(concluido: false, t2);
    var terceira = await LerAsync();
    Assert.Equal("AguardandoExpedicao", terceira.Pedido.Status);
    Assert.Null(terceira.Pedido.DataConclusao);
    Assert.Null(terceira.Agrupamento.DataConclusao);
  });

  [Fact]
  public Task Gravar_fechamento_grava_o_status_e_a_data_dos_Agrupamentos() => NoCenarioAsync(async c =>
  {
    var agora = new DateTime(2026, 10, 5, 8, 30, 0, DateTimeKind.Utc);
    var jaConcluido = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    var ids = new List<int>();
    await using (var escrita = NovoContexto())
    {
      // Tres Agrupamentos alem do que a arvore ja traz: um concluido, um aberto e um que a lista NAO cita.
      foreach (var (codigo, conclusao) in new (string, DateTime?)[] { ("AG-C", null), ("AG-A", agora), ("AG-FORA", jaConcluido) })
      {
        var ag = new Agrupamento
        {
          PedidoId = c.Arvore.PedidoId, Codigo = codigo, Tipo = "Avulso", DataConclusao = conclusao,
          CriadoPorUsuarioId = c.Arvore.AutorId, CriadoEm = DateTime.UtcNow,
        };
        escrita.Agrupamentos.Add(ag);
        await escrita.SaveChangesAsync();
        ids.Add(ag.Id);
      }
    }
    var (concluido, aberto, fora) = (ids[0], ids[1], ids[2]);
    try
    {
      await using (var db = NovoContexto())
        await new ExecucaoRepository(db).GravarFechamentoAsync(
            new FechamentoCalculado(c.Arvore.PedidoId, "AguardandoExpedicao", false,
                [new AgrupamentoNoFechamento(concluido, true), new AgrupamentoNoFechamento(aberto, false)]),
            agora, CancellationToken.None);

      await using var leitura = NovoContexto();
      var pedido = await LerPedidoAsync(leitura, c.Arvore.PedidoId);
      Assert.Equal("AguardandoExpedicao", pedido.Status);
      Assert.Null(pedido.DataConclusao);

      var agrupamentos = await leitura.Agrupamentos.AsNoTracking()
          .Where(a => a.PedidoId == c.Arvore.PedidoId).ToDictionaryAsync(a => a.Id);
      Assert.Equal(agora, agrupamentos[concluido].DataConclusao!.Value, TimeSpan.FromSeconds(1));
      Assert.Null(agrupamentos[aberto].DataConclusao);   // estava concluido e a lista o reabre
      // Quem nao esta na lista nao e tocado, nem o Agrupamento da arvore.
      Assert.Equal(jaConcluido, agrupamentos[fora].DataConclusao!.Value, TimeSpan.FromSeconds(1));
      Assert.Null(agrupamentos[c.Arvore.AgrupamentoId].DataConclusao);
    }
    finally
    {
      await using var limpeza = NovoContexto();
      foreach (var id in ids)
        await limpeza.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Agrupamento WHERE Id = {id}");
    }
  });

  [Fact]
  public Task Gravar_fechamento_nao_toca_Agrupamento_de_outro_Pedido() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var outra = await ArvoreDeTesteNoBanco.CriarAsync(db, "exprepoo");
    try
    {
      await new ExecucaoRepository(db).GravarFechamentoAsync(
          new FechamentoCalculado(c.Arvore.PedidoId, "Concluido", true, [new AgrupamentoNoFechamento(outra.AgrupamentoId, true)]),
          DateTime.UtcNow, CancellationToken.None);

      await using var leitura = NovoContexto();
      Assert.Null((await leitura.Agrupamentos.AsNoTracking().SingleAsync(a => a.Id == outra.AgrupamentoId)).DataConclusao);
    }
    finally { await outra.LimparAsync(NovoContexto); }
  });
}

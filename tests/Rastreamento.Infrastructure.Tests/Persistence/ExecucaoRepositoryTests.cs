using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
  public Task Nos_de_Kit_e_Setores_com_UtilizaKit_vem_do_banco() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);

    var antes = await repo.ListarNosDeKitAsync(c.Nos, CancellationToken.None);
    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Agrupamento SET Tipo = 'Kit' WHERE Id = {c.Arvore.AgrupamentoId}");
    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Setor SET UtilizaKit = 1 WHERE Id = {c.Solda}");
    var depois = await repo.ListarNosDeKitAsync(c.Nos, CancellationToken.None);
    var setores = await repo.ListarSetoresComKitAsync(CancellationToken.None);

    Assert.Empty(antes);
    Assert.Equal(c.Nos.OrderBy(i => i), depois.OrderBy(i => i));
    Assert.Contains(c.Solda, setores);
    Assert.DoesNotContain(c.Corte, setores);
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
  public Task Inicio_da_montagem_e_o_do_pai_nunca_a_baixa_e_nulo_sem_ele() => NoCenarioAsync(async c =>
  {
    var naSolda = Local.AguardandoMontagem(c.Solda);
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(c.Corte, 1), 2m));
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Termino, Local.NoSetor(c.Corte, 1), Local.AguardandoColeta(c.Corte, 1), 2m));
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(c.Corte, 1), naSolda, 2m));
    var comInicio = await GravarAsync(new Montagem
    {
      EstruturaItemId = c.Peca, SetorId = c.Solda, Quantidade = 1m, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    });
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Montagem, naSolda, Local.Montado, 2m, montagemId: comInicio));
    var inicio = await GravarAsync(Mov(c, c.Peca, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(c.Solda, 1), 1m,
        montagemId: comInicio));
    var semInicio = await GravarAsync(new Montagem
    {
      EstruturaItemId = c.Peca, SetorId = c.Solda, Quantidade = 1m, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    });

    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);

    Assert.Equal(inicio, (await repo.ObterInicioDaMontagemAsync(comInicio, CancellationToken.None))!.Id);
    Assert.Null(await repo.ObterInicioDaMontagemAsync(semInicio, CancellationToken.None));
  });

  [Fact]
  public Task Registros_estornaveis_do_Setor_sao_inicios_e_terminos_nao_estornados_e_montagens_validas() => NoCenarioAsync(async c =>
  {
    var corte1 = Local.NoSetor(c.Corte, 1);
    var coleta1 = Local.AguardandoColeta(c.Corte, 1);
    var naSolda = Local.AguardandoMontagem(c.Solda);
    var inicioA = await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Inicio, Local.AIniciar, corte1, 5m));
    var terminoA = await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Termino, corte1, coleta1, 2m));
    var estornado = await GravarAsync(Mov(c, c.ItemB, TiposDeMovimentacao.Inicio, Local.AIniciar, corte1, 3m));
    await GravarAsync(Mov(c, c.ItemB, TiposDeMovimentacao.Estorno, corte1, Local.AIniciar, 3m, estornoDeId: estornado));
    var entrega = await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Entrega, coleta1, naSolda, 2m));   // destino Solda, e e Entrega
    var valida = await GravarAsync(new Montagem
    {
      EstruturaItemId = c.Peca, SetorId = c.Solda, Quantidade = 1m, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    });
    var inicioDoPai = await GravarAsync(Mov(c, c.Peca, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(c.Solda, 1), 1m,
        montagemId: valida));
    var estornada = await GravarAsync(new Montagem
    {
      EstruturaItemId = c.Peca, SetorId = c.Solda, Quantidade = 1m, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
      EstornadaEm = DateTime.UtcNow, EstornadaPorUsuarioId = c.Arvore.AutorId,
    });

    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);
    var noCorte = await repo.ListarRegistrosEstornaveisDoSetorAsync(c.Corte, c.Nos, CancellationToken.None);
    var naSoldaRegistros = await repo.ListarRegistrosEstornaveisDoSetorAsync(c.Solda, c.Nos, CancellationToken.None);

    Assert.Equal(new[] { inicioA, terminoA }, noCorte.Movimentos.Select(m => m.Id).ToArray());   // sem o estornado
    Assert.Empty(noCorte.Montagens);
    Assert.DoesNotContain(naSoldaRegistros.Movimentos, m => m.Id == entrega || m.Id == inicioDoPai);   // Entrega e Inicio com Montagem
    Assert.Equal(new[] { valida }, naSoldaRegistros.Montagens.Select(g => g.Id).ToArray());   // sem a estornada
    Assert.DoesNotContain(naSoldaRegistros.Montagens, g => g.Id == estornada);
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

  /// <summary>O trabalho de um Inicio da Peca do cenario: grava a linha e poe o Pedido em producao.</summary>
  private static async Task IniciarAPecaAsync(Cenario c, ExecucaoRepository repo)
  {
    repo.Adicionar(Mov(c, c.Peca, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(c.Solda, 1), 1m));
    await repo.SalvarAlteracoesAsync(CancellationToken.None);
    await repo.MarcarPedidoEmProducaoAsync(c.Arvore.PedidoId, CancellationToken.None);
  }

  [Fact]
  public Task Transacao_que_nao_confirma_desfaz_o_que_foi_salvo() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);

    var devolvido = await repo.EmTransacaoAsync(async () =>
    {
      await IniciarAPecaAsync(c, repo);
      return "falha";
    }, _ => false, CancellationToken.None);

    Assert.Equal("falha", devolvido);
    await using var novo = NovoContexto();
    Assert.Equal(0, await novo.Movimentacoes.CountAsync(m => m.EstruturaItemId == c.Peca));
    Assert.Equal("Aberto", (await novo.Pedidos.AsNoTracking().SingleAsync(p => p.Id == c.Arvore.PedidoId)).Status);
  });

  [Fact]
  public Task Transacao_que_nao_confirma_limpa_o_change_tracker() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);

    await repo.EmTransacaoAsync(async () =>
    {
      await IniciarAPecaAsync(c, repo);
      repo.Adicionar(Mov(c, c.Peca, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(c.Solda, 1), 2m));   // pendente, sem salvar
      return 0;
    }, _ => false, CancellationToken.None);

    Assert.Empty(db.ChangeTracker.Entries());
    await repo.SalvarAlteracoesAsync(CancellationToken.None);   // nada pendente: nao grava o que sobrou
    await using var novo = NovoContexto();
    Assert.Equal(0, await novo.Movimentacoes.CountAsync(m => m.EstruturaItemId == c.Peca));
  });

  [Fact]
  public Task Transacao_sem_confirmar_continua_commitando() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);

    await repo.EmTransacaoAsync(async () =>
    {
      await IniciarAPecaAsync(c, repo);
      return 0;
    }, CancellationToken.None);

    await using var novo = NovoContexto();
    Assert.Equal(1, await novo.Movimentacoes.CountAsync(m => m.EstruturaItemId == c.Peca));
    Assert.Equal("EmProducao", (await novo.Pedidos.AsNoTracking().SingleAsync(p => p.Id == c.Arvore.PedidoId)).Status);
  });

  [Fact]
  public Task Pedido_Aberto_passa_a_EmProducao_e_outro_status_nao_muda() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);

    await repo.MarcarPedidoEmProducaoAsync(c.Arvore.PedidoId, CancellationToken.None);
    var numero = (await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == c.Arvore.PedidoId)).Numero;
    Assert.Equal(new PedidoDoNo(c.Arvore.PedidoId, numero, "EmProducao", false), await repo.ObterPedidoDoNoAsync(c.ItemA, CancellationToken.None));

    await db.Pedidos.Where(p => p.Id == c.Arvore.PedidoId)
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Concluido"));
    await repo.MarcarPedidoEmProducaoAsync(c.Arvore.PedidoId, CancellationToken.None);
    Assert.Equal("Concluido", (await repo.ObterPedidoDoNoAsync(c.ItemA, CancellationToken.None))!.Status);
  });

  [Fact]
  public Task ObterPedidoDoNo_diz_se_o_Pedido_esta_pausado_e_deixa_de_dizer_depois_de_fechada() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);
    Assert.False((await repo.ObterPedidoDoNoAsync(c.ItemA, CancellationToken.None))!.Pausado);

    var pausa = new PedidoPausa { PedidoId = c.Arvore.PedidoId, PausadoEm = DateTime.UtcNow, PausadoPorUsuarioId = c.Arvore.AutorId };
    repo.Adicionar(pausa);
    await repo.SalvarAlteracoesAsync(CancellationToken.None);
    Assert.True((await repo.ObterPedidoDoNoAsync(c.ItemA, CancellationToken.None))!.Pausado);

    await repo.FecharPausaAsync(pausa.Id, c.Arvore.AutorId, DateTime.UtcNow.AddMinutes(1), CancellationToken.None);
    Assert.False((await repo.ObterPedidoDoNoAsync(c.ItemA, CancellationToken.None))!.Pausado);
  });

  [Fact]
  public Task Fechar_a_pausa_nao_sobrescreve_um_fecho_ja_gravado() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);
    var pausa = new PedidoPausa { PedidoId = c.Arvore.PedidoId, PausadoEm = DateTime.UtcNow, PausadoPorUsuarioId = c.Arvore.AutorId };
    repo.Adicionar(pausa);
    await repo.SalvarAlteracoesAsync(CancellationToken.None);
    var primeiro = DateTime.UtcNow.AddMinutes(1);

    await repo.FecharPausaAsync(pausa.Id, c.Arvore.AutorId, primeiro, CancellationToken.None);
    await repo.FecharPausaAsync(pausa.Id, c.Arvore.AutorId, primeiro.AddHours(1), CancellationToken.None);

    await using var leitura = NovoContexto();
    var lida = await leitura.PedidoPausas.AsNoTracking().SingleAsync(p => p.Id == pausa.Id);
    Assert.Equal(primeiro, lida.RetomadoEm!.Value, TimeSpan.FromSeconds(1));
  });

  [Fact]
  public Task Nos_em_producao_trazem_a_pausa_aberta_com_o_nome_de_quem_pausou_so_nos_do_Pedido_pausado() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var outra = await ArvoreDeTesteNoBanco.CriarAsync(db, "exec2");
    try
    {
      var noDoOutro = await outra.NovaPecaAsync(db, 1m);
      var repo = new ExecucaoRepository(db);
      repo.Adicionar(new PedidoPausa
      {
        PedidoId = c.Arvore.PedidoId, PausadoEm = DateTime.UtcNow, PausadoPorUsuarioId = c.Arvore.AutorId, Motivo = "urgente",
      });
      await repo.SalvarAlteracoesAsync(CancellationToken.None);
      var nomeDeQuemPausou = (await db.Usuarios.AsNoTracking().SingleAsync(u => u.Id == c.Arvore.AutorId)).NomeCompleto;

      var contexto = (await repo.ListarNosEmProducaoAsync(CancellationToken.None))
          .Where(x => c.Nos.Contains(x.No.Id) || x.No.Id == noDoOutro).ToList();

      var pausados = contexto.Where(x => c.Nos.Contains(x.No.Id)).ToList();
      Assert.Equal(3, pausados.Count);
      Assert.All(pausados, x =>
      {
        Assert.Equal(c.Arvore.PedidoId, x.Pausa!.PedidoId);
        Assert.Equal((nomeDeQuemPausou, "urgente"), (x.Pausa.PausadoPorNome, x.Pausa.Motivo));
      });
      Assert.Null(Assert.Single(contexto, x => x.No.Id == noDoOutro).Pausa);
    }
    finally
    {
      await outra.LimparAsync(NovoContexto);
    }
  });

  [Fact]
  public Task Nos_em_producao_trazem_o_cliente_e_os_materiais_do_proprio_no() => NoCenarioAsync(async c =>
  {
    await using var escrita = NovoContexto();
    var primeiro = await c.Arvore.NovoMaterialAsync(escrita, "Chapa A");
    var segundo = await c.Arvore.NovoMaterialAsync(escrita, "Chapa B");
    // Gravados na ordem inversa da dos codigos: a ordem que sai e a do Codigo, nao a da gravacao.
    await c.Arvore.MaterialNoNoAsync(escrita, c.ItemA, segundo);
    await c.Arvore.MaterialNoNoAsync(escrita, c.ItemA, primeiro);

    await using var db = NovoContexto();
    var contexto = (await new ExecucaoRepository(db).ListarNosEmProducaoAsync(CancellationToken.None))
        .Where(x => c.Nos.Contains(x.No.Id)).ToList();

    Assert.Equal(3, contexto.Count);
    Assert.All(contexto, x => Assert.Equal("Cliente de teste", x.PedidoCliente));
    Assert.Equal(
        new[] { primeiro, segundo },
        Assert.Single(contexto, x => x.No.Id == c.ItemA).Materiais.Select(m => m.Id).ToArray());
    Assert.Equal(
        new[] { "Chapa A", "Chapa B" },
        Assert.Single(contexto, x => x.No.Id == c.ItemA).Materiais.Select(m => m.Descricao).ToArray());
    Assert.Empty(Assert.Single(contexto, x => x.No.Id == c.ItemB).Materiais);
    Assert.Empty(Assert.Single(contexto, x => x.No.Id == c.Peca).Materiais);
  });

  [Fact]
  public Task Materiais_dos_nos_em_producao_saem_numa_consulta_so() => NoCenarioAsync(async c =>
  {
    await using var escrita = NovoContexto();
    var material = await c.Arvore.NovoMaterialAsync(escrita, "Chapa");
    foreach (var no in c.Nos) await c.Arvore.MaterialNoNoAsync(escrita, no, material);

    var contador = new ContadorDeComandos();
    var opcoes = new DbContextOptionsBuilder<RastreamentoDbContext>().UseSqlServer(Conn).AddInterceptors(contador).Options;
    await using var db = new RastreamentoDbContext(opcoes);
    var repo = new ExecucaoRepository(db);

    contador.Zerar();
    await repo.ListarNosEmProducaoAsync(CancellationToken.None);
    var comTres = contador.Comandos;

    var novos = new List<int>();
    for (var i = 0; i < 3; i++)
    {
      var item = await c.Arvore.NovoItemAsync(escrita, c.Peca, 1m, 1m);
      await c.Arvore.MaterialNoNoAsync(escrita, item, material);
      novos.Add(item);
    }
    contador.Zerar();
    var contexto = await repo.ListarNosEmProducaoAsync(CancellationToken.None);
    var comSeis = contador.Comandos;

    Assert.All(novos.Concat(c.Nos), id =>
        Assert.Equal(material, Assert.Single(Assert.Single(contexto, x => x.No.Id == id).Materiais).Id));
    Assert.True(comTres > 0);
    Assert.Equal(comTres, comSeis);
  });

  /// <summary>Conta os comandos que abrem leitor (SELECT) no contexto em que foi registrado.</summary>
  private sealed class ContadorDeComandos : DbCommandInterceptor
  {
    private int _comandos;

    public int Comandos => _comandos;

    public void Zerar() => Interlocked.Exchange(ref _comandos, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
      Interlocked.Increment(ref _comandos);
      return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
      Interlocked.Increment(ref _comandos);
      return new ValueTask<InterceptionResult<DbDataReader>>(result);
    }
  }

  [Fact]
  public Task Travar_Pedido_fora_de_transacao_lanca_e_dentro_devolve_o_Pedido() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);
    await Assert.ThrowsAsync<InvalidOperationException>(() => repo.TravarPedidoAsync(c.Arvore.PedidoId, CancellationToken.None));

    var travado = await repo.EmTransacaoAsync(
        () => repo.TravarPedidoAsync(c.Arvore.PedidoId, CancellationToken.None), CancellationToken.None);
    var inexistente = await repo.EmTransacaoAsync(
        () => repo.TravarPedidoAsync(int.MaxValue, CancellationToken.None), CancellationToken.None);

    Assert.Equal((c.Arvore.PedidoId, "Aberto"), (travado!.Id, travado.Status));
    Assert.Null(inexistente);
  });

  [Fact]
  public Task TravarPedidosDosNosAsync_devolve_os_Pedidos_distintos_em_ordem_crescente() => NoCenarioAsync(async c =>
  {
    await using var escrita = NovoContexto();
    var outra = await ArvoreDeTesteNoBanco.CriarAsync(escrita, "exec");
    try
    {
      var pecaDaOutra = await outra.NovaPecaAsync(escrita, 1m);
      await using var db = NovoContexto();
      var repo = new ExecucaoRepository(db);

      // Os nos do Pedido mais novo primeiro, e dois nos do mesmo Pedido: a ordem e a unicidade sao do metodo.
      var pedidos = await repo.EmTransacaoAsync(
          () => repo.TravarPedidosDosNosAsync([pecaDaOutra, c.ItemB, c.Peca, int.MaxValue], CancellationToken.None),
          CancellationToken.None);

      Assert.True(c.Arvore.PedidoId < outra.PedidoId);
      Assert.Equal(new[] { c.Arvore.PedidoId, outra.PedidoId }, pedidos.ToArray());
    }
    finally
    {
      await outra.LimparAsync(NovoContexto);
    }
  });

  [Fact]
  public Task TravarPedidosDosNosAsync_segura_a_linha_do_Pedido() => NoCenarioAsync(async c =>
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
      await repoA.TravarPedidosDosNosAsync([c.ItemA], CancellationToken.None);
      await Assert.ThrowsAsync<ConflitoDeConcorrenciaException>(() => repoB.EmTransacaoAsync(
          () => repoB.TravarPedidoAsync(c.Arvore.PedidoId, CancellationToken.None), CancellationToken.None));
      return 0;
    }, CancellationToken.None);
  });

  [Fact]
  public Task TravarPedidosDosNosAsync_fora_de_transacao_lanca() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => new ExecucaoRepository(db).TravarPedidosDosNosAsync([c.Peca], CancellationToken.None));
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

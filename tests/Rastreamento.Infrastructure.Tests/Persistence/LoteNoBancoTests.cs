using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Iniciar em lote contra o SQL Server real, com o caso de uso de producao e os repositorios reais, como
/// `CorridaNoIniciarTests`: o que so o banco prova — a recusa no segundo item desfaz o que o primeiro
/// gravou de verdade, e dois lotes concorrentes em ordem inversa nunca deixam meio lote — e o lote no
/// teto. Toda asercao escopada nos nos da propria arvore.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class LoteNoBancoTests : TesteComBanco
{
  private readonly ITestOutputHelper _saida;

  public LoteNoBancoTests(ITestOutputHelper saida) => _saida = saida;

  private static ApontamentoUseCase CasoDeUso(RastreamentoDbContext db) =>
      new(new ExecucaoRepository(db), new EstruturaRepository(db), new SetorRepository(db), new ReceitaPadraoRepository(db));

  private static LoteDeInicioDto Inicios(int setor, params (int No, decimal Quantidade)[] itens) =>
      new(setor, itens.Select(i => new ItemDeInicioDto(i.No, i.Quantidade)).ToList());

  [Fact]
  public async Task Lote_recusado_no_segundo_item_nao_deixa_nada_do_primeiro()
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "lote");
    try
    {
      var corte = await arvore.NovoSetorAsync(db);
      var pai = await arvore.NovaPecaAsync(db, 1m);
      var filho = await arvore.NovoItemAsync(db, pai, 1m, 1m);
      var q = await arvore.NovaPecaAsync(db, 2m);
      await arvore.RoteiroAsync(db, pai, corte);
      await arvore.RoteiroAsync(db, filho, corte);
      await arvore.RoteiroAsync(db, q, corte);

      // O filho chega a aguardar a montagem do pai no Corte pelos casos de uso reais.
      await using (var arranjo = NovoContexto())
      {
        var ct = CancellationToken.None;
        Assert.True((await CasoDeUso(arranjo).Iniciar(filho, new InicioDto(corte, 1m), arvore.AutorId, ct)).Sucesso);
        Assert.True((await CasoDeUso(arranjo).Terminar(filho, new TerminoDto(corte, 1, 1m), arvore.AutorId, ct)).Sucesso);
        var entrega = await new EntregaUseCase(new ExecucaoRepository(arranjo), new EstruturaRepository(arranjo),
                new ReceitaPadraoRepository(arranjo))
            .Entregar(new EntregaDto([new ItemDaEntregaDto(filho, new OrigemDaEntregaDto(Posicoes.AguardandoColeta, corte, 1), null, 1m)]),
                arvore.AutorId, ct);
        Assert.True(entrega.Sucesso);
        // O Inicio do filho ja pos o Pedido em producao (regra 28). Volta a `Aberto` so no Pedido, fora do
        // livro, para o teste provar tambem que o Inicio do pai recusado nao deixa o `EmProducao`.
        await arranjo.Pedidos.Where(p => p.Id == arvore.PedidoId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Aberto"));
      }
      await using (var antes = NovoContexto())
        Assert.Equal(3, await antes.Movimentacoes.CountAsync(m => m.EstruturaItemId == filho));

      Result<IReadOnlyList<MovimentacaoDto>> r;
      await using (var lote = NovoContexto())
        r = await CasoDeUso(lote).IniciarEmLote(Inicios(corte, (pai, 1m), (q, 5m)), arvore.AutorId, CancellationToken.None);

      Assert.Equal(CodigosDaExecucao.SaldoInsuficiente, r.Erro);
      await using var leitura = NovoContexto();
      Assert.False(await leitura.Montagens.AnyAsync(g => g.EstruturaItemId == pai));
      Assert.False(await leitura.Movimentacoes.AnyAsync(m => m.EstruturaItemId == pai || m.EstruturaItemId == q));
      // Do filho, so os tres do arranjo: nenhuma baixa de montagem.
      Assert.Equal(
          new[] { TiposDeMovimentacao.Inicio, TiposDeMovimentacao.Termino, TiposDeMovimentacao.Entrega },
          await leitura.Movimentacoes.Where(m => m.EstruturaItemId == filho).OrderBy(m => m.Id).Select(m => m.Tipo).ToArrayAsync());
      Assert.Equal("Aberto", (await leitura.Pedidos.AsNoTracking().SingleAsync(p => p.Id == arvore.PedidoId)).Status);
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }

  [Fact]
  public async Task Dois_lotes_em_ordem_inversa_sobre_os_mesmos_nos_nunca_gravam_meio_lote()
  {
    // Os dois lotes podem ou nao se sobrepor numa dada execucao; a propriedade afirmada vale nos dois
    // casos (um espera o outro e ve o saldo zerado, ou um e derrubado), e por isso o teste nao fica
    // intermitente — o mesmo argumento de `CorridaNoIniciarTests`.
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "lote");
    try
    {
      var corte = await arvore.NovoSetorAsync(db);
      var a = await arvore.NovaPecaAsync(db, 1m);
      var b = await arvore.NovaPecaAsync(db, 1m);
      await arvore.RoteiroAsync(db, a, corte);
      await arvore.RoteiroAsync(db, b, corte);

      async Task<Result<IReadOnlyList<MovimentacaoDto>>> LoteAsync(int primeiro, int segundo)
      {
        await using var contexto = NovoContexto();
        return await CasoDeUso(contexto).IniciarEmLote(
            Inicios(corte, (primeiro, 1m), (segundo, 1m)), arvore.AutorId, CancellationToken.None);
      }

      var resultados = await Task.WhenAll(Task.Run(() => LoteAsync(a, b)), Task.Run(() => LoteAsync(b, a)));

      Assert.Single(resultados, r => r.Sucesso);
      Assert.Contains(resultados.Single(r => !r.Sucesso).Erro,
          new[] { CodigosDaExecucao.SaldoInsuficiente, CodigosDaExecucao.ConflitoDeConcorrencia });
      await using var leitura = NovoContexto();
      var inicios = await leitura.Movimentacoes
          .Where(m => (m.EstruturaItemId == a || m.EstruturaItemId == b) && m.Tipo == TiposDeMovimentacao.Inicio)
          .Select(m => m.EstruturaItemId).ToListAsync();
      Assert.Equal(new[] { a, b }, inicios.Order().ToArray());
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }

  [Fact]
  public async Task Lote_no_teto_grava_os_cem_itens()
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "lote");
    try
    {
      var corte = await arvore.NovoSetorAsync(db);
      var pecas = new List<int>();
      for (var i = 0; i < ApontamentoUseCase.TamanhoMaximoDoLote; i++)
      {
        var peca = await arvore.NovaPecaAsync(db, 1m);
        await arvore.RoteiroAsync(db, peca, corte);
        pecas.Add(peca);
      }

      await using var lote = NovoContexto();
      var cronometro = Stopwatch.StartNew();
      var r = await CasoDeUso(lote).IniciarEmLote(
          Inicios(corte, pecas.Select(p => (p, 1m)).ToArray()), arvore.AutorId, CancellationToken.None);
      cronometro.Stop();
      // Medida para o registro, nao asercao: o tempo depende da maquina.
      _saida.WriteLine($"IniciarEmLote de {pecas.Count} itens: {cronometro.ElapsedMilliseconds} ms");

      Assert.True(r.Sucesso, r.Detalhe ?? r.Erro);
      Assert.Equal(pecas, r.Valor!.Select(m => m.EstruturaItemId).ToList());
      await using var leitura = NovoContexto();
      Assert.Equal(pecas.Count, await leitura.Movimentacoes
          .CountAsync(m => pecas.Contains(m.EstruturaItemId) && m.Tipo == TiposDeMovimentacao.Inicio));
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }
}

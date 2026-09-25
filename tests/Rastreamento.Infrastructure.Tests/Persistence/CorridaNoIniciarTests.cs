using Microsoft.EntityFrameworkCore;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Concorrencia do "iniciar" contra o SQL Server real (spec da Fase 3, secao 9.2), com o caso de uso de
/// producao e os repositorios reais — nenhuma logica de transacao reimplementada aqui. O primeiro teste
/// e deterministico (a trava de um contexto segura o outro ate o timeout); o segundo e a corrida de
/// verdade, que pode ou nao se sobrepor numa dada execucao — a propriedade que ele afirma vale nos dois
/// casos, e e por isso que ele nao fica intermitente.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class CorridaNoIniciarTests : TesteComBanco
{
  private static ApontamentoUseCase CasoDeUso(RastreamentoDbContext db) =>
      new(new ExecucaoRepository(db), new EstruturaRepository(db), new SetorRepository(db), new ReceitaPadraoRepository(db));

  [Fact]
  public async Task Iniciar_espera_a_trava_do_no_e_o_timeout_vira_ConflitoDeConcorrencia()
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "corr");
    try
    {
      var corte = await arvore.NovoSetorAsync(db);
      var peca = await arvore.NovaPecaAsync(db, 10m);
      await arvore.RoteiroAsync(db, peca, corte);

      await using var dbA = NovoContexto();
      await using var dbB = NovoContexto();
      var repoA = new ExecucaoRepository(dbA);
      await dbB.Database.OpenConnectionAsync();
      await dbB.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 300");

      Result<MovimentacaoDto> enquantoTravado = null!;
      await repoA.EmTransacaoAsync(async () =>
      {
        await repoA.TravarNosAsync([peca], CancellationToken.None);
        enquantoTravado = await CasoDeUso(dbB).Iniciar(peca, new InicioDto(corte, 1m), arvore.AutorId, CancellationToken.None);
        return 0;
      }, CancellationToken.None);
      var depois = await CasoDeUso(dbB).Iniciar(peca, new InicioDto(corte, 1m), arvore.AutorId, CancellationToken.None);

      Assert.Equal(CodigosDaExecucao.ConflitoDeConcorrencia, enquantoTravado.Erro);
      Assert.True(depois.Sucesso);
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }

  [Fact]
  public async Task Dois_inicios_paralelos_com_saldo_para_um_so_nunca_passam_da_quantidade()
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "corr");
    try
    {
      var corte = await arvore.NovoSetorAsync(db);
      var peca = await arvore.NovaPecaAsync(db, 10m);
      await arvore.RoteiroAsync(db, peca, corte);

      async Task<Result<MovimentacaoDto>> IniciarSeteAsync()
      {
        await using var contexto = NovoContexto();
        return await CasoDeUso(contexto).Iniciar(peca, new InicioDto(corte, 7m), arvore.AutorId, CancellationToken.None);
      }

      var resultados = await Task.WhenAll(Task.Run(IniciarSeteAsync), Task.Run(IniciarSeteAsync));

      Assert.Single(resultados, r => r.Sucesso);
      Assert.Contains(resultados.Single(r => !r.Sucesso).Erro,
          new[] { CodigosDaExecucao.SaldoInsuficiente, CodigosDaExecucao.ConflitoDeConcorrencia });
      await using var leitura = NovoContexto();
      Assert.Equal(7m, await leitura.Movimentacoes.Where(m => m.EstruturaItemId == peca).SumAsync(m => m.Quantidade));
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }
}

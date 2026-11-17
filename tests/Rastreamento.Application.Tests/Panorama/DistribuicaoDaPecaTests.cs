using Rastreamento.Application.Execucao;
using Rastreamento.Application.Panorama;
using Xunit;

namespace Rastreamento.Application.Tests.Panorama;

/// <summary>A distribuicao do "Onde esta" (C8 da spec do Panorama): a ordem do Roteiro, nao a da calculadora.</summary>
public class DistribuicaoDaPecaTests
{
  private const int Corte = 1, Solda = 3;

  private static IReadOnlyList<(Local, decimal)> Saldos(params (Local, decimal)[] s) => s;

  [Fact]
  public void Peca_dividida_sai_na_ordem_do_roteiro()
  {
    var r = DistribuicaoDaPeca.Montar(Saldos(
        (Local.NoSetor(Corte, 2), 3m), (Local.NoSetor(Solda, 1), 10m), (Local.AIniciar, 2m)));

    // O passo 1 e o do Setor de Id maior: a ordem e a do Roteiro, nao a do Id do Setor.
    Assert.Equal(
        [TiposDeSegmento.AIniciar, TiposDeSegmento.Passo, TiposDeSegmento.Passo],
        r.Select(s => s.Tipo));
    Assert.Equal([null, Solda, Corte], r.Select(s => s.SetorId));
    Assert.Equal([null, 1, 2], r.Select(s => s.Ordem));
    Assert.Equal([2m, 10m, 3m], r.Select(s => s.Quantidade));
  }

  [Fact]
  public void Aguardando_coleta_soma_no_passo_e_vem_a_parte()
  {
    var r = DistribuicaoDaPeca.Montar(Saldos((Local.NoSetor(Corte, 1), 3m), (Local.AguardandoColeta(Corte, 1), 1m)));

    var s = Assert.Single(r);
    Assert.Equal(new Segmento(TiposDeSegmento.Passo, Corte, 1, 4m, 1m), s);
  }

  [Fact]
  public void Retorno_ao_setor_da_dois_segmentos()
  {
    var r = DistribuicaoDaPeca.Montar(Saldos((Local.NoSetor(Corte, 3), 2m), (Local.NoSetor(Corte, 1), 5m)));

    Assert.Equal(2, r.Count);
    Assert.All(r, s => Assert.Equal(Corte, s.SetorId));
    Assert.Equal([1, 3], r.Select(s => s.Ordem));
  }

  [Fact]
  public void Conserto_soma_as_tres_posicoes_por_setor()
  {
    var r = DistribuicaoDaPeca.Montar(Saldos(
        (Local.ConsertoPronto(Solda), 1m), (Local.AguardandoConserto(Corte), 2m), (Local.EmConserto(Corte), 3m)));

    Assert.Equal(
        [new Segmento(TiposDeSegmento.Conserto, Corte, null, 5m, 0m), new Segmento(TiposDeSegmento.Conserto, Solda, null, 1m, 0m)],
        r);
  }

  [Fact]
  public void Local_de_expedicao_soma_as_cinco_posicoes()
  {
    var r = DistribuicaoDaPeca.Montar(Saldos(
        (Local.NaExpedicao, 1m), (Local.Conforme, 2m), (Local.NaoConforme, 3m),
        (Local.AguardandoLiberacao, 4m), (Local.Liberada, 5m)));

    Assert.Equal([new Segmento(TiposDeSegmento.Expedicao, null, null, 15m, 0m)], r);
  }

  [Fact]
  public void Expedido_e_perdido_vem_por_ultimo()
  {
    var r = DistribuicaoDaPeca.Montar(Saldos((Local.Perdido, 1m), (Local.Expedido, 2m), (Local.Conforme, 3m), (Local.AIniciar, 4m)));

    Assert.Equal(
        [TiposDeSegmento.AIniciar, TiposDeSegmento.Expedicao, TiposDeSegmento.Expedido, TiposDeSegmento.Perdido],
        r.Select(s => s.Tipo));
  }

  [Fact]
  public void Segmento_com_zero_nao_aparece()
  {
    var r = DistribuicaoDaPeca.Montar(Saldos((Local.AIniciar, 0m), (Local.NoSetor(Corte, 1), 0m), (Local.Conforme, 0m), (Local.Expedido, 2m)));

    Assert.Equal([TiposDeSegmento.Expedido], r.Select(s => s.Tipo));
  }

  [Fact]
  public void Soma_dos_segmentos_e_a_quantidade()
  {
    var r = DistribuicaoDaPeca.Montar(Saldos(
        (Local.AIniciar, 1m), (Local.NoSetor(Corte, 1), 2m), (Local.AguardandoColeta(Corte, 1), 3m),
        (Local.AguardandoConserto(Solda), 4m), (Local.EmConserto(Solda), 5m), (Local.ConsertoPronto(Corte), 6m),
        (Local.NaExpedicao, 7m), (Local.Liberada, 8m), (Local.Expedido, 9m), (Local.Perdido, 10m)));

    Assert.Equal(55m, r.Sum(s => s.Quantidade));
  }

  [Fact]
  public void Aguardando_montagem_e_montado_sao_ignorados()
  {
    // So filho vai a montagem: numa Peca essas posicoes nao ocorrem, e a funcao nao as inventa.
    var r = DistribuicaoDaPeca.Montar(Saldos((Local.AguardandoMontagem(Corte), 2m), (Local.Montado, 3m), (Local.AIniciar, 1m)));

    Assert.Equal([TiposDeSegmento.AIniciar], r.Select(s => s.Tipo));
  }
}

using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Xunit;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// A calculadora da spec da Fase 3, secao 7, por tabela. Os numeros da spec do Kit (C de 10, D de 45
/// com razao 4) entram como casos, inclusive os contraexemplos (a) e (b) da review de branch do Kit.
/// </summary>
public class CalculadoraDeExecucaoTests
{
  private const int Corte = 1, Dobra = 2, Solda = 3, Pintura = 4;

  private static NoDoCalculo No(int id, int? pai, decimal quantidade, decimal? razao, params int[] setores) =>
      new(id, pai, quantidade, razao, setores.Select((s, i) => new PassoDoCalculo(s, i + 1)).ToList());

  private static SaldoLiquido Em(int item, Local local, decimal quantidade) =>
      new(item, local.Posicao, local.SetorId, local.Ordem, quantidade);

  private static CalculadoraDeExecucao Calcular(
      IEnumerable<NoDoCalculo> nos,
      IEnumerable<SaldoLiquido>? saldos = null,
      IReadOnlyDictionary<int, decimal>? montados = null,
      IEnumerable<(int, int)>? alcancados = null) =>
      new(nos, saldos ?? Array.Empty<SaldoLiquido>(), montados ?? new Dictionary<int, decimal>(),
          alcancados ?? Array.Empty<(int, int)>());

  [Fact]
  public void Saldo_a_iniciar_e_a_quantidade_menos_o_que_saiu()
  {
    var calc = Calcular(
        [No(1, null, 10m, null, Corte)],
        [Em(1, Local.AIniciar, -4m), Em(1, Local.NoSetor(Corte, 1), 4m)]);

    Assert.Equal(6m, calc.Saldo(1, Local.AIniciar));
    Assert.Equal(4m, calc.Saldo(1, Local.NoSetor(Corte, 1)));
    Assert.Equal(4m, calc.SaidoDeAIniciar(1));
    Assert.Equal(new[] { (Local.AIniciar, 6m), (Local.NoSetor(Corte, 1), 4m) }, calc.Saldos(1));
  }

  [Fact]
  public void Destino_segue_o_passo_mesmo_com_setor_repetido()
  {
    var calc = Calcular([No(1, null, 10m, null, Corte, Dobra, Corte)]);

    Assert.Equal(TipoDeDestino.ProximoPasso, calc.DestinoDaColeta(1, 1).Tipo);
    Assert.Equal<PassoDoCalculo?>(new PassoDoCalculo(Dobra, 2), calc.DestinoDaColeta(1, 1).Passo);
    Assert.Equal<PassoDoCalculo?>(new PassoDoCalculo(Corte, 3), calc.DestinoDaColeta(1, 2).Passo);
    Assert.Equal(TipoDeDestino.Expedicao, calc.DestinoDaColeta(1, 3).Tipo);   // Peca no fim: expedicao
  }

  [Fact]
  public void Item_no_ultimo_passo_vai_para_a_montagem_do_pai_com_os_Setores_do_Roteiro_dele()
  {
    var calc = Calcular([No(1, null, 10m, null, Corte, Solda, Pintura, Solda), No(2, 1, 40m, 4m, Corte)]);

    var destino = calc.DestinoDaColeta(2, 1);

    Assert.Equal(TipoDeDestino.Montagem, destino.Tipo);
    Assert.Equal(1, destino.PaiId);
    Assert.Equal(new[] { Corte, Solda, Pintura }, destino.SetoresPossiveis);   // distintos, em ordem de passo
    Assert.False(destino.PaiSemRoteiro);
  }

  [Fact]
  public void Sugestao_e_o_Setor_onde_o_pai_esta_em_trabalho_o_de_maior_saldo()
  {
    var calc = Calcular(
        [No(1, null, 10m, null, Corte, Solda, Pintura), No(2, 1, 40m, 4m, Dobra)],
        [Em(1, Local.NoSetor(Solda, 2), 3m), Em(1, Local.NoSetor(Pintura, 3), 5m)]);

    Assert.Equal(Pintura, calc.DestinoDaColeta(2, 1).SugestaoSetorId);
  }

  [Fact]
  public void Sem_pai_em_trabalho_a_sugestao_e_o_primeiro_passo_nao_alcancado()
  {
    var calc = Calcular(
        [No(1, null, 10m, null, Corte, Solda, Pintura), No(2, 1, 40m, 4m, Dobra)],
        alcancados: [(1, 1)]);

    Assert.Equal(Solda, calc.DestinoDaColeta(2, 1).SugestaoSetorId);
  }

  [Fact]
  public void Pai_sem_Roteiro_nao_tem_sugestao_nem_Setores()
  {
    var calc = Calcular([No(1, null, 10m, null), No(2, 1, 40m, 4m, Dobra)]);

    var destino = calc.DestinoDaColeta(2, 1);

    Assert.Equal(TipoDeDestino.Montagem, destino.Tipo);
    Assert.Null(destino.SugestaoSetorId);
    Assert.Empty(destino.SetoresPossiveis);
    Assert.True(destino.PaiSemRoteiro);
  }

  [Fact]
  public void Peca_no_ultimo_passo_e_tarefa_inteira_para_a_expedicao()
  {
    var calc = Calcular(
        [No(1, null, 10m, null, Corte)],
        [Em(1, Local.AIniciar, -10m), Em(1, Local.AguardandoColeta(Corte, 1), 10m)]);

    Assert.Equal(10m, calc.Tarefa(1, Corte, 1));
    Assert.Equal(0m, calc.SobraDaColeta(1, Corte, 1));
  }

  [Fact]
  public void Contraexemplo_b_da_spec_do_Kit_a_sobra_de_D_nao_vira_tarefa()
  {
    // P de 10; C de 10 (razao 1) e D de 45 (razao 4) terminaram o ultimo passo, no Corte.
    var calc = Calcular(
        [No(1, null, 10m, null, Solda), No(2, 1, 10m, 1m, Corte), No(3, 1, 45m, 4m, Corte)],
        [
          Em(2, Local.AIniciar, -10m), Em(2, Local.AguardandoColeta(Corte, 1), 10m),
          Em(3, Local.AIniciar, -45m), Em(3, Local.AguardandoColeta(Corte, 1), 45m),
        ]);

    Assert.Equal(10m, calc.Tarefa(2, Corte, 1));
    Assert.Equal(40m, calc.Tarefa(3, Corte, 1));
    Assert.Equal(5m, calc.SobraDaColeta(3, Corte, 1));
    Assert.Equal(0m, calc.SobraDaColeta(2, Corte, 1));
  }

  [Fact]
  public void Depois_de_montar_tudo_o_que_sobrou_nao_e_tarefa_nem_da_para_montar()
  {
    var calc = Calcular(
        [No(1, null, 10m, null, Solda), No(2, 1, 10m, 1m, Corte), No(3, 1, 45m, 4m, Corte)],
        [
          Em(2, Local.AIniciar, -10m), Em(2, Local.Montado, 10m),
          Em(3, Local.AIniciar, -45m), Em(3, Local.Montado, 40m), Em(3, Local.AguardandoColeta(Corte, 1), 5m),
        ],
        montados: new Dictionary<int, decimal> { [1] = 10m });

    Assert.Equal(0m, calc.FaltaMontar(1));
    Assert.Equal(0m, calc.Precisa(3));
    Assert.Equal(0m, calc.Tarefa(3, Corte, 1));
    Assert.Equal(5m, calc.SobraDaColeta(3, Corte, 1));
    Assert.Equal(0m, calc.CalcularMontabilidade(1, Solda).DaParaMontar);
  }

  [Fact]
  public void Contraexemplo_a_o_excesso_em_montagem_aparece_no_nivel_do_no()
  {
    // O Movimentador levou as 45 D para a Solda (a entrega nao tem teto, spec secao 4.3).
    var calc = Calcular(
        [No(1, null, 10m, null, Solda), No(2, 1, 10m, 1m, Corte), No(3, 1, 45m, 4m, Corte)],
        [
          Em(2, Local.AIniciar, -10m), Em(2, Local.AguardandoMontagem(Solda), 10m),
          Em(3, Local.AIniciar, -45m), Em(3, Local.AguardandoMontagem(Solda), 45m),
        ]);

    Assert.Equal(5m, calc.ExcessoEmMontagem(3));
    Assert.Equal(0m, calc.ExcessoEmMontagem(2));
    Assert.Equal(0m, calc.Precisa(3));

    var montavel = calc.CalcularMontabilidade(1, Solda);
    Assert.Equal(10m, montavel.DaParaMontar);            // min(10, 10/1, floor(45/4) = 11)
    Assert.All(montavel.Filhos, f => Assert.Null(f.FaltaParaProxima));   // 11 > o que falta montar
  }

  [Fact]
  public void Da_para_montar_N_e_falta_X_de_Y_para_a_proxima()
  {
    var calc = Calcular(
        [No(1, null, 5m, null, Solda), No(2, 1, 10m, 2m, Corte), No(3, 1, 5m, 1m, Corte)],
        [Em(2, Local.AguardandoMontagem(Solda), 5m), Em(3, Local.AguardandoMontagem(Solda), 1m)]);

    var montavel = calc.CalcularMontabilidade(1, Solda);

    Assert.Equal(1m, montavel.DaParaMontar);   // min(5, floor(5/2) = 2, floor(1/1) = 1)
    Assert.Equal(5m, montavel.FaltaMontar);
    var b = montavel.Filhos.Single(f => f.FilhoId == 2);
    var c = montavel.Filhos.Single(f => f.FilhoId == 3);
    Assert.Equal((5m, 4m, 0m), (b.Presente, b.NecessarioParaProxima!.Value, b.FaltaParaProxima!.Value));
    Assert.Equal((1m, 2m, 1m), (c.Presente, c.NecessarioParaProxima!.Value, c.FaltaParaProxima!.Value));
  }

  [Fact]
  public void Filho_ausente_do_Setor_aparece_com_presente_zero()
  {
    var calc = Calcular(
        [No(1, null, 5m, null, Solda), No(2, 1, 10m, 2m, Corte), No(3, 1, 5m, 1m, Corte)],
        [Em(2, Local.AguardandoMontagem(Solda), 4m)]);

    var montavel = calc.CalcularMontabilidade(1, Solda);

    Assert.Equal(0m, montavel.DaParaMontar);
    var c = montavel.Filhos.Single(f => f.FilhoId == 3);
    Assert.Equal(0m, c.Presente);
    Assert.Equal(1m, c.FaltaParaProxima);
  }

  [Fact]
  public void Falta_nao_aparece_quando_nao_ha_proxima_unidade_a_montar()
  {
    var calc = Calcular(
        [No(1, null, 2m, null, Solda), No(2, 1, 2m, 1m, Corte)],
        [Em(2, Local.AguardandoMontagem(Solda), 3m)],
        montados: new Dictionary<int, decimal> { [1] = 1m });

    var montavel = calc.CalcularMontabilidade(1, Solda);

    Assert.Equal(1m, montavel.DaParaMontar);
    Assert.Null(Assert.Single(montavel.Filhos).NecessarioParaProxima);
  }

  [Fact]
  public void Razao_editada_depois_da_montagem_so_muda_o_futuro()
  {
    // P de 5 com 2 ja montadas; a razao de C foi editada de 4 para 3 depois disso. As baixas gravadas
    // (8 = 2 x 4) nao entram na conta: o que falta receber usa a razao NOVA sobre o que falta montar.
    var calc = Calcular(
        [No(1, null, 5m, null, Solda), No(2, 1, 20m, 3m, Corte)],
        [Em(2, Local.AIniciar, -18m), Em(2, Local.Montado, 8m), Em(2, Local.AguardandoMontagem(Solda), 10m)],
        montados: new Dictionary<int, decimal> { [1] = 2m });

    Assert.Equal(3m, calc.FaltaMontar(1));
    Assert.Equal(0m, calc.Precisa(2));             // max(0, 3 x 3 - 10)
    Assert.Equal(1m, calc.ExcessoEmMontagem(2));   // 10 - 3 x 3
  }

  [Fact]
  public void Coletas_pendentes_listam_so_tarefa_positiva()
  {
    var calc = Calcular(
        [No(1, null, 10m, null, Corte, Dobra), No(2, 1, 5m, 1m, Corte)],
        [
          Em(1, Local.AIniciar, -4m), Em(1, Local.AguardandoColeta(Corte, 1), 4m),
          Em(2, Local.AIniciar, -5m), Em(2, Local.AguardandoColeta(Corte, 1), 5m),
        ],
        montados: new Dictionary<int, decimal> { [1] = 10m });   // o pai ja foi todo montado

    var pendentes = calc.ColetasPendentes();

    Assert.Equal(new ColetaPendente(1, Corte, 1, 4m), Assert.Single(pendentes));
  }
}

using Rastreamento.Application.Baixa;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Baixa;

/// <summary>
/// A conta da baixa no pai que recebe em conjunto (C15 e secao 3.5 da spec da Fase 5C): o resto do conjunto quebrado e
/// sobra, nao cobre o pai, e por isso a baixa nao descarta o "a iniciar" que o substitui. Arvore: Peca 1 de 10, que
/// comeca na Solda (`UtilizaKit`); filho 2 de 20 (razao 2), do Corte; filho 3 de 10 (razao 1), da Dobra. Tres conjuntos
/// entraram: 6 do filho 2 e 3 do filho 3 aguardam montagem na Solda, e o resto esta a iniciar. Os auxiliares sao copia
/// dos de `CalculoDaBaixaTests`, com `DeKit` e os Setores com `UtilizaKit`.
/// </summary>
public class CalculoDaBaixaComKitTests
{
  private const int Corte = 1, Dobra = 2, Solda = 3;

  private static string NomeDoSetor(int id) => id switch { Corte => "Corte", Dobra => "Dobra", _ => "Solda" };

  private static string NomePeloId(int id) => $"No {id}";

  private static NoDoCalculo No(int id, int? pai, decimal quantidade, decimal? razao, bool deKit, params int[] setores) =>
      new(id, pai, quantidade, razao, setores.Select((s, i) => new PassoDoCalculo(s, i + 1)).ToList(), deKit);

  private static SaldoLiquido Em(int item, Local local, decimal quantidade) =>
      new(item, local.Posicao, local.SetorId, local.Ordem, quantidade);

  private static CalculadoraDeExecucao Calcular(
      IEnumerable<NoDoCalculo> nos, IEnumerable<SaldoLiquido> saldos, IReadOnlySet<int>? setoresComKit = null,
      IReadOnlyDictionary<int, decimal>? montados = null, IReadOnlySet<int>? saidosDoProjeto = null) =>
      new(nos, saldos, montados ?? new Dictionary<int, decimal>(), Array.Empty<(int, int)>(),
          setoresComKit: setoresComKit, saidosDoProjeto: saidosDoProjeto);

  private static OrigemDaBaixa De(Local local, decimal quantidade) => new(local, quantidade);

  private static PedidoDeBaixa Parte(int noId, IReadOnlyList<int> cadeia, params OrigemDaBaixa[] origens) =>
      new(noId, MotivosDePerda.PerdaArmazem, false, origens, false, cadeia);

  private static PedidoDeBaixa ParteComTopo(int noId, IReadOnlyList<int> cadeia, params OrigemDaBaixa[] origens) =>
      new(noId, MotivosDePerda.PerdaArmazem, false, origens, true, cadeia);

  private static PedidoDeBaixa Tudo(int noId, string motivo, IReadOnlyList<int> cadeia) =>
      new(noId, motivo, true, Array.Empty<OrigemDaBaixa>(), false, cadeia);

  private static PlanoDaBaixa Planejar(CalculadoraDeExecucao calc, PedidoDeBaixa pedido)
  {
    var r = CalculoDaBaixa.Planejar(calc, pedido, NomePeloId, NomeDoSetor);
    Assert.True(r.Sucesso, $"{r.Erro}: {r.Detalhe}");
    return r.Valor!;
  }

  private static (int NoId, Local Origem, decimal Quantidade)[] Linhas(PlanoDaBaixa plano) =>
      plano.Linhas.Select(l => (l.NoId, l.Origem, l.Quantidade)).ToArray();

  private static NoDoCalculo[] Arvore(bool kit) =>
      [No(1, null, 10m, null, kit, Solda), No(2, 1, 20m, 2m, kit, Corte), No(3, 1, 10m, 1m, kit, Dobra)];

  private static SaldoLiquido[] TresConjuntosNaSolda() =>
  [
    Em(2, Local.AIniciar, -6m), Em(2, Local.AguardandoMontagem(Solda), 6m),
    Em(3, Local.AIniciar, -3m), Em(3, Local.AguardandoMontagem(Solda), 3m),
  ];

  private static CalculadoraDeExecucao Kit(IEnumerable<SaldoLiquido>? saldos = null) =>
      Calcular(Arvore(kit: true), saldos ?? TresConjuntosNaSolda(), new HashSet<int> { Solda });

  private static CalculadoraDeExecucao Avulso() => Calcular(Arvore(kit: false), TresConjuntosNaSolda());

  [Fact]
  public void Perda_dentro_da_Solda_com_topo_nao_descarta_o_a_iniciar_dos_filhos()
  {
    var plano = Planejar(Kit(), ParteComTopo(2, [1], De(Local.AguardandoMontagem(Solda), 1m)));

    Assert.Equal(1m, plano.Trava!.Quantidade);
    // Nada de 2 nem de 3 a iniciar: os 14 e os 7 que la estao fecham, com os 2 conjuntos inteiros, as 9 unidades.
    Assert.Equal([(2, Local.AguardandoMontagem(Solda), 1m), (1, Local.AIniciar, 1m)], Linhas(plano));
    Assert.Equal([(2, 1m), (3, 1m)], plano.ViramSobra);
  }

  [Fact]
  public void Perda_dentro_da_Solda_sem_topo_lista_o_resto_como_sobra()
  {
    var plano = Planejar(Kit(), Parte(2, [1], De(Local.AguardandoMontagem(Solda), 1m)));

    Assert.Equal(1m, plano.Trava!.Quantidade);
    Assert.Equal([(2, Local.AguardandoMontagem(Solda), 1m)], Linhas(plano));
    Assert.Equal([(2, 1m), (3, 1m)], plano.ViramSobra);
  }

  [Fact]
  public void A_mesma_baixa_num_Avulso_da_o_resultado_de_hoje()
  {
    var plano = Planejar(Avulso(), ParteComTopo(2, [1], De(Local.AguardandoMontagem(Solda), 1m)));

    // O topo sai de "a iniciar" e a cascata tira 1 de 2 e 1 de 3 a iniciar: o excesso sobre o que o pai de 9 pede.
    Assert.Equal(
        [
          (2, Local.AguardandoMontagem(Solda), 1m), (1, Local.AIniciar, 1m), (2, Local.AIniciar, 1m),
          (3, Local.AIniciar, 1m),
        ],
        Linhas(plano));
    Assert.Empty(plano.ViramSobra);
  }

  [Fact]
  public void Perda_de_Pecas_a_iniciar_faz_sobrar_os_conjuntos_inteiros_que_o_Kit_nao_pede_mais()
  {
    // Oito Pecas saem de "a iniciar": o pai so monta 2, e dos 3 conjuntos inteiros a espera sobra 1 (2 do filho 2 e 1
    // do filho 3). Todo o "a iniciar" dos filhos sai junto: os 2 conjuntos que valem ja cobrem o pai.
    var plano = Planejar(Kit(), Parte(1, [], De(Local.AIniciar, 8m)));

    Assert.Equal([(1, Local.AIniciar, 8m), (2, Local.AIniciar, 14m), (3, Local.AIniciar, 7m)], Linhas(plano));
    Assert.Equal([(2, 2m), (3, 1m)], plano.ViramSobra);
  }

  [Fact]
  public void Fabricado_fora_da_Solda_cobre_o_Kit_antes_do_a_iniciar()
  {
    // Alem dos 3 conjuntos, o filho 2 tem 2 aguardando coleta no Corte. Perdida 1 do filho 2 na Solda com o topo: o pai
    // de 9 pede 18; valem 4 da Solda e os 2 do Corte, e faltam 12 dos 12 a iniciar. Nada a iniciar sai.
    var calc = Kit(
    [
      Em(2, Local.AIniciar, -8m), Em(2, Local.AguardandoMontagem(Solda), 6m), Em(2, Local.AguardandoColeta(Corte, 1), 2m),
      Em(3, Local.AIniciar, -3m), Em(3, Local.AguardandoMontagem(Solda), 3m),
    ]);

    var plano = Planejar(calc, ParteComTopo(2, [1], De(Local.AguardandoMontagem(Solda), 1m)));

    Assert.Equal([(2, Local.AguardandoMontagem(Solda), 1m), (1, Local.AIniciar, 1m)], Linhas(plano));
    Assert.Equal([(2, 1m), (3, 1m)], plano.ViramSobra);
  }

  [Fact]
  public void Descartar_tudo_de_um_filho_do_Kit_nao_lista_os_irmaos_como_sobra()
  {
    // O filho 2 descartado por inteiro sai do projeto (C22 da spec da Fase 5B-1) e deixa de contar para os conjuntos:
    // os 3 do filho 3 que estao na Solda continuam valendo.
    var plano = Planejar(Kit(), Tudo(2, MotivosDePerda.Descarte, [1]));

    Assert.Equal([(2, Local.AIniciar, 14m), (2, Local.AguardandoMontagem(Solda), 6m)], Linhas(plano));
    Assert.Empty(plano.ViramSobra);
  }

  [Fact]
  public void Perder_tudo_de_um_filho_do_Kit_lista_a_espera_dos_irmaos_como_sobra()
  {
    // Por perda o filho 2 continua no projeto: sem ele na Solda nenhum conjunto fecha, e os 3 do filho 3 sao sobra.
    var plano = Planejar(Kit(), Tudo(2, MotivosDePerda.PerdaArmazem, [1]));

    Assert.Equal([(3, 3m)], plano.ViramSobra);
  }
}

using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Xunit;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// A calculadora com a baixa da Fase 5B-1: o perdido de "a iniciar" do pai sai do que falta montar (C5 da spec da
/// Fase 5B-1) e o filho descartado por inteiro sai da conta da montagem (C22).
/// </summary>
public class CalculadoraComPerdaTests
{
  private const int Corte = 1, Solda = 3;

  private static NoDoCalculo No(int id, int? pai, decimal quantidade, decimal? razao, bool deKit = false, params int[] setores) =>
      new(id, pai, quantidade, razao, setores.Select((s, i) => new PassoDoCalculo(s, i + 1)).ToList(), deKit);

  private static SaldoLiquido Em(int item, Local local, decimal quantidade) =>
      new(item, local.Posicao, local.SetorId, local.Ordem, quantidade);

  private static CalculadoraDeExecucao Calcular(
      IEnumerable<NoDoCalculo> nos,
      IEnumerable<SaldoLiquido>? saldos = null,
      IReadOnlyDictionary<int, decimal>? montados = null,
      IReadOnlyDictionary<int, decimal>? perdidosDeAIniciar = null,
      IReadOnlySet<int>? saidos = null,
      IReadOnlySet<int>? setoresComKit = null) =>
      new(nos, saldos ?? Array.Empty<SaldoLiquido>(), montados ?? new Dictionary<int, decimal>(),
          Array.Empty<(int, int)>(), setoresComKit, perdidosDeAIniciar, saidos);

  [Fact]
  public void FaltaMontar_desconta_o_perdido_de_a_iniciar()
  {
    var calc = Calcular(
        [No(1, null, 10m, null, false, Solda), No(2, 1, 20m, 2m, false, Corte)],
        [Em(2, Local.AguardandoMontagem(Solda), 3m)],
        new Dictionary<int, decimal> { [1] = 2m },
        new Dictionary<int, decimal> { [1] = 4m });

    Assert.Equal(4m, calc.PerdidoDeAIniciar(1));
    Assert.Equal(4m, calc.FaltaMontar(1));
    Assert.Equal(8m - 3m, calc.Precisa(2));
  }

  [Fact]
  public void Perda_do_pai_depois_de_iniciado_nao_muda_o_que_falta_montar()
  {
    // 10 montados; uma perda em NoSetor nao entra em `perdidosDeAIniciar`.
    var calc = Calcular(
        [No(1, null, 10m, null, false, Solda), No(2, 1, 20m, 2m, false, Corte)],
        [Em(1, Local.NoSetor(Solda, 1), -1m), Em(1, Local.Perdido, 1m)],
        new Dictionary<int, decimal> { [1] = 10m });

    Assert.Equal(0m, calc.PerdidoDeAIniciar(1));
    Assert.Equal(0m, calc.FaltaMontar(1));
  }

  [Fact]
  public void Filho_que_saiu_do_projeto_nao_limita_a_montabilidade()
  {
    var calc = Calcular(
        [No(1, null, 10m, null, false, Solda), No(2, 1, 10m, 1m, false, Corte), No(3, 1, 10m, 1m, false, Corte)],
        [Em(3, Local.AguardandoMontagem(Solda), 6m)],
        saidos: new HashSet<int> { 2 });

    var m = calc.CalcularMontabilidade(1, Solda);

    Assert.True(calc.SaiuDoProjeto(2));
    Assert.False(calc.SaiuDoProjeto(3));
    Assert.Equal(6m, m.DaParaMontar);
    Assert.Equal([3], m.Filhos.Select(f => f.FilhoId).ToArray());
    Assert.Equal([3], calc.FilhosQueContam(1).Select(f => f.Id).ToArray());
    Assert.Equal(2, calc.Filhos(1).Count);   // a arvore continua inteira
  }

  [Fact]
  public void Filho_que_saiu_do_projeto_nao_conta_nos_conjuntos_a_espera()
  {
    var calc = Calcular(
        [No(1, null, 10m, null, true, Solda), No(2, 1, 10m, 1m, true, Corte), No(3, 1, 10m, 1m, true, Corte)],
        [Em(2, Local.AguardandoMontagem(Solda), 5m), Em(3, Local.AguardandoMontagem(Solda), 2m)],
        saidos: new HashSet<int> { 2 },
        setoresComKit: new HashSet<int> { Solda });

    Assert.Equal(2m, calc.ConjuntosAEspera(1));
  }

  [Fact]
  public void EmProducao_nao_soma_montado_expedido_nem_perdido()
  {
    var calc = Calcular(
        [No(1, null, 10m, null, false, Corte)],
        [Em(1, Local.AIniciar, -7m), Em(1, Local.NoSetor(Corte, 1), 2m), Em(1, Local.AguardandoColeta(Corte, 1), 1m),
         Em(1, Local.Montado, 1m), Em(1, Local.Expedido, 2m), Em(1, Local.Perdido, 2m)]);

    // a iniciar 3 + no Setor 2 + aguardando coleta 1
    Assert.Equal(6m, calc.EmProducao(1));
  }
}

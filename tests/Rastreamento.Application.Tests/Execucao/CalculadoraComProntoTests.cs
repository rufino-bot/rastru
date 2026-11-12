using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Xunit;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// A calculadora com o no pronto da Fase 5B-2: o `Pronto` de um no com filhos conta como montado (C17 da spec da Fase
/// 5B-2; decisao P3 do plano de backend) e o que um `Pronto` pos em `AguardandoMontagem` de um Setor com `UtilizaKit`
/// forma um conjunto em formacao, que os irmaos entram para completar (D8; decisao P4).
/// </summary>
public class CalculadoraComProntoTests
{
  private const int Corte = 1, Dobra = 2, Solda = 3;

  private static NoDoCalculo No(int id, int? pai, decimal quantidade, decimal? razao, bool deKit = false, params int[] setores) =>
      new(id, pai, quantidade, razao, setores.Select((s, i) => new PassoDoCalculo(s, i + 1)).ToList(), deKit);

  private static SaldoLiquido Em(int item, Local local, decimal quantidade) =>
      new(item, local.Posicao, local.SetorId, local.Ordem, quantidade);

  private static CalculadoraDeExecucao Calcular(
      IEnumerable<NoDoCalculo> nos,
      IEnumerable<SaldoLiquido>? saldos = null,
      IReadOnlyDictionary<int, decimal>? montados = null,
      IReadOnlyDictionary<int, decimal>? prontos = null,
      IReadOnlySet<int>? comProntoEmMontagem = null,
      IReadOnlySet<int>? setoresComKit = null,
      IReadOnlyDictionary<int, decimal>? perdidosDeAIniciar = null) =>
      new(nos, saldos ?? Array.Empty<SaldoLiquido>(), montados ?? new Dictionary<int, decimal>(),
          Array.Empty<(int, int)>(), setoresComKit, perdidosDeAIniciar,
          prontos: prontos, comProntoEmMontagem: comProntoEmMontagem);

  /// <summary>
  /// Pai Kit de 1 que comeca na Solda (`UtilizaKit`); A (2) e B (3) de razao 1, que terminam no Corte e na Dobra.
  /// A chegou a Solda por um `Pronto` (ou por uma `Entrega`, quando `comPronto` e falso); `bNaColeta` unidades de B
  /// aguardam coleta no ultimo passo dele.
  /// </summary>
  private static CalculadoraDeExecucao KitComAEmMontagem(bool comPronto, decimal bNaColeta = 0m)
  {
    var saldos = new List<SaldoLiquido>
    {
      Em(2, Local.AIniciar, -1m), Em(2, Local.AguardandoMontagem(Solda), 1m),
    };
    if (bNaColeta > 0m)
      saldos.AddRange([Em(3, Local.AIniciar, -bNaColeta), Em(3, Local.AguardandoColeta(Dobra, 1), bNaColeta)]);
    return Calcular(
        [No(1, null, 1m, null, true, Solda), No(2, 1, 1m, 1m, true, Corte), No(3, 1, 1m, 1m, true, Dobra)],
        saldos,
        prontos: comPronto ? new Dictionary<int, decimal> { [2] = 1m } : null,
        comProntoEmMontagem: comPronto ? new HashSet<int> { 2 } : null,
        setoresComKit: new HashSet<int> { Solda });
  }

  [Fact]
  public void Pronto_de_no_com_filhos_soma_ao_total_montado()
  {
    // Pai de 3, com 1 pronto aguardando coleta; o filho (razao 4) ja tem 3 aguardando montagem na Solda.
    var calc = Calcular(
        [No(1, null, 3m, null, false, Solda), No(2, 1, 12m, 4m, false, Corte)],
        [Em(1, Local.AIniciar, -1m), Em(1, Local.AguardandoColeta(Solda, 1), 1m),
         Em(2, Local.AIniciar, -3m), Em(2, Local.AguardandoMontagem(Solda), 3m)],
        prontos: new Dictionary<int, decimal> { [1] = 1m });

    Assert.Equal(1m, calc.Pronto(1));
    Assert.Equal(1m, calc.TotalMontado(1));
    Assert.Equal(2m, calc.FaltaMontar(1));
    Assert.Equal(8m - 3m, calc.Precisa(2));
    // A identidade da Fase 3D continua: o que saiu de "a iniciar" do pai e o total montado dele.
    Assert.Equal(calc.SaidoDeAIniciar(1), calc.TotalMontado(1));
  }

  [Fact]
  public void Pronto_de_no_com_filhos_soma_as_montagens()
  {
    var calc = Calcular(
        [No(1, null, 5m, null, false, Solda), No(2, 1, 5m, 1m, false, Corte)],
        montados: new Dictionary<int, decimal> { [1] = 2m },
        prontos: new Dictionary<int, decimal> { [1] = 1m });

    Assert.Equal(3m, calc.TotalMontado(1));
    Assert.Equal(2m, calc.FaltaMontar(1));
  }

  [Fact]
  public void Pronto_de_folha_nao_mexe_no_total_montado()
  {
    var calc = Calcular(
        [No(1, null, 3m, null, false, Solda), No(2, 1, 6m, 2m, false, Corte)],
        [Em(2, Local.AIniciar, -2m), Em(2, Local.AguardandoColeta(Corte, 1), 2m)],
        prontos: new Dictionary<int, decimal> { [2] = 2m });

    Assert.Equal(2m, calc.Pronto(2));
    Assert.Equal(0m, calc.TotalMontado(2));
    Assert.Equal(0m, calc.TotalMontado(1));
    Assert.Equal(3m, calc.FaltaMontar(1));
    Assert.Equal(6m, calc.Precisa(2));
  }

  [Fact]
  public void Pronto_em_montagem_de_Kit_forma_conjunto_em_formacao()
  {
    var calc = KitComAEmMontagem(comPronto: true);

    Assert.Equal(1m, calc.ConjuntosAEspera(1));
    Assert.Equal(1m, calc.ConjuntosEmFormacao(1));
    Assert.Equal(1m, calc.ComplementoDoConjunto(1, 3));
    Assert.Equal(0m, calc.ComplementoDoConjunto(1, 2));
    // O conjunto em formacao ja esta na espera: o teto de conjuntos novos continua zero.
    Assert.Equal(0m, calc.TetoDeEntrada(1));
  }

  [Fact]
  public void Sem_pronto_nao_ha_conjunto_em_formacao()
  {
    var calc = KitComAEmMontagem(comPronto: false);

    // C2 da spec da Fase 5C: sem pronto e sem o irmao, o A sozinho nao fecha conjunto; e sobra.
    Assert.Equal(0m, calc.ConjuntosAEspera(1));
    Assert.Equal(0m, calc.ConjuntosEmFormacao(1));
    Assert.Equal(0m, calc.ComplementoDoConjunto(1, 3));
  }

  [Fact]
  public void Conjunto_em_formacao_desconta_os_conjuntos_ja_completos()
  {
    // Pai Kit de 3; A tem 2 na Solda (um pronto), B tem 1: um conjunto completo e um em formacao. B completa com 1.
    var calc = Calcular(
        [No(1, null, 3m, null, true, Solda), No(2, 1, 3m, 1m, true, Corte), No(3, 1, 3m, 1m, true, Dobra)],
        [Em(2, Local.AIniciar, -2m), Em(2, Local.AguardandoMontagem(Solda), 2m),
         Em(3, Local.AIniciar, -1m), Em(3, Local.AguardandoMontagem(Solda), 1m)],
        prontos: new Dictionary<int, decimal> { [2] = 1m },
        comProntoEmMontagem: new HashSet<int> { 2 },
        setoresComKit: new HashSet<int> { Solda });

    Assert.Equal(2m, calc.ConjuntosAEspera(1));
    Assert.Equal(1m, calc.ConjuntosEmFormacao(1));
    Assert.Equal(1m, calc.ComplementoDoConjunto(1, 3));
    Assert.Equal(0m, calc.ComplementoDoConjunto(1, 2));
    Assert.Equal(1m, calc.TetoDeEntrada(1));
  }

  [Fact]
  public void Conjunto_em_formacao_nao_passa_do_que_o_pai_ainda_precisa()
  {
    // Pai Kit de 2, que perdeu 1 de "a iniciar" depois do pronto: so precisa de 1 conjunto. A tem 2 prontos na Solda
    // (dois conjuntos em formacao) e B tem 2 aguardando coleta. So um conjunto vale (regra 25): B completa com 1.
    var calc = Calcular(
        [No(1, null, 2m, null, true, Solda), No(2, 1, 2m, 1m, true, Corte), No(3, 1, 2m, 1m, true, Dobra)],
        [Em(1, Local.AIniciar, -1m), Em(1, Local.Perdido, 1m),
         Em(2, Local.AIniciar, -2m), Em(2, Local.AguardandoMontagem(Solda), 2m),
         Em(3, Local.AIniciar, -2m), Em(3, Local.AguardandoColeta(Dobra, 1), 2m)],
        prontos: new Dictionary<int, decimal> { [2] = 2m },
        comProntoEmMontagem: new HashSet<int> { 2 },
        setoresComKit: new HashSet<int> { Solda },
        perdidosDeAIniciar: new Dictionary<int, decimal> { [1] = 1m });

    Assert.Equal(1m, calc.FaltaMontar(1));
    Assert.Equal(2m, calc.ConjuntosAEspera(1));
    Assert.Equal(1m, calc.ConjuntosEmFormacao(1));
    Assert.Equal(1m, calc.ComplementoDoConjunto(1, 3));
    Assert.Equal(0m, calc.ComplementoDoConjunto(1, 2));
    Assert.Equal(0m, calc.TetoDeEntrada(1));
    Assert.Equal(new ColetaPendente(3, Dobra, 1, 1m), Assert.Single(calc.ColetasPendentes()));
    Assert.Empty(calc.KitsDaColeta());
  }

  [Fact]
  public void Pai_que_nao_recebe_em_conjunto_nao_tem_conjunto_em_formacao()
  {
    // O mesmo estado num Agrupamento Avulso: a regra 25 nao vale, e nada se forma.
    var calc = Calcular(
        [No(1, null, 1m, null, false, Solda), No(2, 1, 1m, 1m, false, Corte), No(3, 1, 1m, 1m, false, Dobra)],
        [Em(2, Local.AIniciar, -1m), Em(2, Local.AguardandoMontagem(Solda), 1m)],
        prontos: new Dictionary<int, decimal> { [2] = 1m },
        comProntoEmMontagem: new HashSet<int> { 2 },
        setoresComKit: new HashSet<int> { Solda });

    Assert.Equal(0m, calc.ConjuntosEmFormacao(1));
    Assert.Equal(0m, calc.ComplementoDoConjunto(1, 3));
  }

  [Fact]
  public void ColetasPendentes_oferece_o_complemento_e_o_cartao_do_Kit_nao_conta_ele()
  {
    var calc = KitComAEmMontagem(comPronto: true, bNaColeta: 1m);

    var coleta = Assert.Single(calc.ColetasPendentes());
    Assert.Equal(new ColetaPendente(3, Dobra, 1, 1m), coleta);
    Assert.DoesNotContain(calc.KitsDaColeta(), k => k.Filhos.Any(f => f.FilhoId == 3 && f.Pronto > 0m));
  }

  [Fact]
  public void Cartao_do_Kit_mostra_so_o_que_passa_do_complemento()
  {
    // Pai Kit de 2: A pronto 1 na Solda (um conjunto em formacao), B com 2 aguardando coleta. Um B vai como
    // complemento nas coletas; o outro fica no cartao do Kit, que segue incompleto (falta A).
    var calc = Calcular(
        [No(1, null, 2m, null, true, Solda), No(2, 1, 2m, 1m, true, Corte), No(3, 1, 2m, 1m, true, Dobra)],
        [Em(2, Local.AIniciar, -1m), Em(2, Local.AguardandoMontagem(Solda), 1m),
         Em(3, Local.AIniciar, -2m), Em(3, Local.AguardandoColeta(Dobra, 1), 2m)],
        prontos: new Dictionary<int, decimal> { [2] = 1m },
        comProntoEmMontagem: new HashSet<int> { 2 },
        setoresComKit: new HashSet<int> { Solda });

    Assert.Equal(new ColetaPendente(3, Dobra, 1, 1m), Assert.Single(calc.ColetasPendentes()));
    var kit = Assert.Single(calc.KitsDaColeta());
    Assert.Equal(1m, kit.Teto);
    Assert.Equal(1m, kit.Filhos.Single(f => f.FilhoId == 3).Pronto);
    Assert.Equal(0m, kit.Conjuntos);
  }

  [Fact]
  public void Sem_pronto_o_filho_de_Kit_no_ultimo_passo_fica_so_no_cartao()
  {
    var calc = KitComAEmMontagem(comPronto: false, bNaColeta: 1m);

    Assert.Empty(calc.ColetasPendentes());
  }
}

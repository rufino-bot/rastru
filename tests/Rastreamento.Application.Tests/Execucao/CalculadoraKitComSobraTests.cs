using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Xunit;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// O Kit que conta so conjuntos completos (spec da Fase 5C, secao 3): o que aguarda montagem e nao fecha conjunto e
/// sobra, e nao ocupa vaga no teto (C2 a C6, C9 e C16). Arvore base: Peca 1 de 10, Kit, que comeca na Solda
/// (`UtilizaKit`); filho 2 de 20 (razao 2), que termina no Corte; filho 3 de 10 (razao 1), que termina na Dobra.
/// </summary>
public class CalculadoraKitComSobraTests
{
  private const int Corte = 1, Dobra = 2, Solda = 3, Pintura = 4;

  private static NoDoCalculo No(int id, int? pai, decimal quantidade, decimal? razao, bool deKit = false, params int[] setores) =>
      new(id, pai, quantidade, razao, setores.Select((s, i) => new PassoDoCalculo(s, i + 1)).ToList(), deKit);

  private static SaldoLiquido Em(int item, Local local, decimal quantidade) =>
      new(item, local.Posicao, local.SetorId, local.Ordem, quantidade);

  /// <summary>`quantidade` do no aguardando montagem no Setor, saida de "a iniciar".</summary>
  private static SaldoLiquido[] Espera(int item, int setor, decimal quantidade) =>
      [Em(item, Local.AIniciar, -quantidade), Em(item, Local.AguardandoMontagem(setor), quantidade)];

  private static CalculadoraDeExecucao Calcular(
      IEnumerable<NoDoCalculo> nos,
      IEnumerable<SaldoLiquido>? saldos = null,
      IReadOnlyDictionary<int, decimal>? perdidosDeAIniciar = null,
      IReadOnlySet<int>? saidosDoProjeto = null,
      IReadOnlyDictionary<int, decimal>? prontos = null,
      IReadOnlySet<int>? comProntoEmMontagem = null) =>
      new(nos, saldos ?? Array.Empty<SaldoLiquido>(), new Dictionary<int, decimal>(), Array.Empty<(int, int)>(),
          new HashSet<int> { Solda }, perdidosDeAIniciar, saidosDoProjeto, prontos, comProntoEmMontagem);

  private static NoDoCalculo[] Base(decimal peca = 10m, bool kit = true, decimal razaoDo2 = 2m) =>
      [No(1, null, peca, null, kit, Solda), No(2, 1, 20m, razaoDo2, kit, Corte), No(3, 1, 10m, 1m, kit, Dobra)];

  /// <summary>Uma unidade da Peca saiu de "a iniciar" por uma baixa: o pai so tem 9 a montar.</summary>
  private static readonly SaldoLiquido[] UmaPecaPerdida = [Em(1, Local.AIniciar, -1m), Em(1, Local.Perdido, 1m)];

  private static readonly Dictionary<int, decimal> PerdeuUma = new() { [1] = 1m };

  /// <summary>Tres conjuntos entraram e uma unidade do filho 2 se perdeu la dentro, levando uma Peca: 5 e 3 a espera.</summary>
  private static CalculadoraDeExecucao ConjuntoQuebrado() =>
      Calcular(Base(), [.. Espera(2, Solda, 5m), .. Espera(3, Solda, 3m), .. UmaPecaPerdida], PerdeuUma);

  [Fact]
  public void Conjunto_quebrado_vira_sobra_e_nao_ocupa_vaga()
  {
    var calc = ConjuntoQuebrado();

    Assert.Equal(2m, calc.ConjuntosAEspera(1));
    Assert.Equal(7m, calc.TetoDeEntrada(1));
    Assert.Equal(1m, calc.ExcessoEmMontagem(2));
    Assert.Equal(1m, calc.ExcessoEmMontagem(3));
    Assert.Equal(14m, calc.Precisa(2));
    Assert.Equal(7m, calc.Precisa(3));
  }

  [Fact]
  public void Kit_quebrado_no_fim_ainda_recebe_o_ultimo_conjunto()
  {
    // Depois da baixa entraram mais 6 conjuntos: 17 e 9 a espera, nada montado. Contando pelo filho mais adiantado, o
    // teto seria 9 - 9 = 0 e a nona unidade nunca se completaria.
    var calc = Calcular(Base(), [.. Espera(2, Solda, 17m), .. Espera(3, Solda, 9m), .. UmaPecaPerdida], PerdeuUma);

    Assert.Equal(8m, calc.ConjuntosAEspera(1));
    Assert.Equal(1m, calc.TetoDeEntrada(1));
    Assert.Equal(1m, calc.ExcessoEmMontagem(2));
    Assert.Equal(1m, calc.ExcessoEmMontagem(3));
  }

  [Fact]
  public void Espera_desalinhada_da_regra_25_aceita_um_conjunto()
  {
    var calc = Calcular(Base(peca: 2m, razaoDo2: 4m), [.. Espera(2, Solda, 7m), .. Espera(3, Solda, 1m)]);

    Assert.Equal(1m, calc.ConjuntosAEspera(1));
    Assert.Equal(1m, calc.TetoDeEntrada(1));
    Assert.Equal(3m, calc.ExcessoEmMontagem(2));
    Assert.Equal(0m, calc.ExcessoEmMontagem(3));
  }

  [Fact]
  public void Kit_sem_perda_conta_como_antes()
  {
    var calc = Calcular(Base(), [.. Espera(2, Solda, 6m), .. Espera(3, Solda, 3m)]);

    Assert.Equal(3m, calc.ConjuntosAEspera(1));
    Assert.Equal(7m, calc.TetoDeEntrada(1));
    Assert.Equal(0m, calc.ExcessoEmMontagem(2));
    Assert.Equal(14m, calc.Precisa(2));
  }

  [Fact]
  public void Pai_kit_cujos_filhos_sairam_todos_inicia_como_folha()
  {
    var calc = Calcular(Base(), saidosDoProjeto: new HashSet<int> { 2, 3 });

    Assert.True(calc.IniciaComoFolha(1));
    Assert.Equal(10m, calc.CalcularMontabilidade(1, Solda).DaParaMontar);
    Assert.Empty(calc.KitsDaColeta());
  }

  [Fact]
  public void Falta_para_a_proxima_do_kit_ignora_a_sobra()
  {
    var m = ConjuntoQuebrado().CalcularMontabilidade(1, Solda);

    Assert.Equal(2m, m.DaParaMontar);
    // Para a terceira unidade: 3 x 2 - (5 - 1) do filho 2 e 3 x 1 - (3 - 1) do filho 3. O `Presente` e o saldo inteiro.
    Assert.Equal(2m, m.Filhos.Single(f => f.FilhoId == 2).FaltaParaProxima);
    Assert.Equal(1m, m.Filhos.Single(f => f.FilhoId == 3).FaltaParaProxima);
    Assert.Equal(5m, m.Filhos.Single(f => f.FilhoId == 2).Presente);
  }

  [Fact]
  public void TemConjuntoAEspera_so_no_kit_com_algo_na_Solda()
  {
    var kit = Calcular(Base(), Espera(2, Solda, 6m));
    var avulso = Calcular(Base(kit: false), Espera(2, Solda, 6m));
    var soNaPintura = Calcular(Base(), Espera(2, Pintura, 6m));

    Assert.True(kit.TemConjuntoAEspera(1));
    Assert.False(avulso.TemConjuntoAEspera(1));
    Assert.False(soNaPintura.TemConjuntoAEspera(1));
  }

  [Fact]
  public void Pai_com_pronto_em_montagem_nao_ve_o_conjunto_em_formacao_como_sobra()
  {
    // O exemplo da secao 4.5 da spec da Fase 5B-2: Peca de 1; o filho 2 chegou a Solda por um `Pronto` e espera o 3.
    var calc = Calcular(
        [No(1, null, 1m, null, true, Solda), No(2, 1, 1m, 1m, true, Corte), No(3, 1, 1m, 1m, true, Dobra)],
        Espera(2, Solda, 1m),
        prontos: new Dictionary<int, decimal> { [2] = 1m },
        comProntoEmMontagem: new HashSet<int> { 2 });

    Assert.Equal(0m, calc.TetoDeEntrada(1));
    Assert.Equal(1m, calc.ComplementoDoConjunto(1, 3));
    Assert.Equal(0m, calc.ExcessoEmMontagem(2));
  }

  [Fact]
  public void Sobra_por_setor_tira_primeiro_da_Solda_e_depois_de_fora_do_Kit()
  {
    // Filho 2: 5 na Solda e 4 na Pintura (sem `UtilizaKit`); filho 3: 2 na Solda. Dois conjuntos completos.
    SaldoLiquido[] saldos = [.. Espera(2, Solda, 5m), .. Espera(2, Pintura, 4m), .. Espera(3, Solda, 2m)];

    // Peca de 10: sobra 5 - 2 x 2 = 1 na Solda; os 4 da Pintura cabem nos (10 - 2) x 2 que o pai ainda precisa.
    var de10 = Calcular(Base(), saldos);
    Assert.Equal(new[] { (Solda, 1m) }, de10.SobraEmMontagemPorSetor(2));
    Assert.Equal(1m, de10.ExcessoEmMontagem(2));

    // Peca de 3: depois dos 2 contados o pai so precisa de (3 - 2) x 2; dos 4 da Pintura, 2 sao sobra.
    var de3 = Calcular(Base(peca: 3m), saldos);
    Assert.Equal(new[] { (Solda, 1m), (Pintura, 2m) }, de3.SobraEmMontagemPorSetor(2));
    Assert.Equal(3m, de3.ExcessoEmMontagem(2));
  }

  [Fact]
  public void Razao_fracionaria_conta_conjuntos_completos_sem_sobra_fantasma()
  {
    var calc = Calcular(Base(razaoDo2: 0.5m), [.. Espera(2, Solda, 1.5m), .. Espera(3, Solda, 3m)]);

    Assert.Equal(3m, calc.ConjuntosAEspera(1));
    Assert.Equal(0m, calc.ExcessoEmMontagem(2));
    Assert.Equal(0m, calc.ExcessoEmMontagem(3));
  }

  [Fact]
  public void Filho_que_saiu_do_projeto_nao_limita_os_completos()
  {
    var calc = Calcular(Base(), Espera(2, Solda, 6m), saidosDoProjeto: new HashSet<int> { 3 });

    Assert.Equal(3m, calc.ConjuntosAEspera(1));
    Assert.Equal(0m, calc.ExcessoEmMontagem(2));
  }

  [Fact]
  public void Pai_sem_nada_a_montar_tem_toda_a_espera_como_sobra()
  {
    var calc = Calcular(
        Base(),
        [.. Espera(2, Solda, 6m), .. Espera(3, Solda, 3m), Em(1, Local.AIniciar, -10m), Em(1, Local.Perdido, 10m)],
        new Dictionary<int, decimal> { [1] = 10m });

    Assert.Equal(0m, calc.TetoDeEntrada(1));
    Assert.Equal(6m, calc.ExcessoEmMontagem(2));
    Assert.Equal(3m, calc.ExcessoEmMontagem(3));
  }

  [Fact]
  public void Conjunto_em_formacao_alem_do_que_o_pai_precisa_da_o_mesmo_teto_sobra_e_precisa()
  {
    // O estado de `Conjunto_em_formacao_nao_passa_do_que_o_pai_ainda_precisa`, de `CalculadoraComProntoTests`: pai
    // Kit de 2 que perdeu 1 de "a iniciar" (falta 1); o filho 2 tem 2 prontos na Solda, o 3 tem 2 aguardando coleta.
    // `ConjuntosAEspera` soma os em formacao sem o limite do que o pai precisa (2, e nao 1); o teto, a sobra e o
    // `Precisa` tem de dar o mesmo nas duas formas, porque `contados` ja limita ao que falta montar: contados = 1.
    var calc = Calcular(
        [No(1, null, 2m, null, true, Solda), No(2, 1, 2m, 1m, true, Corte), No(3, 1, 2m, 1m, true, Dobra)],
        [.. UmaPecaPerdida, .. Espera(2, Solda, 2m),
         Em(3, Local.AIniciar, -2m), Em(3, Local.AguardandoColeta(Dobra, 1), 2m)],
        PerdeuUma,
        prontos: new Dictionary<int, decimal> { [2] = 2m },
        comProntoEmMontagem: new HashSet<int> { 2 });

    Assert.Equal(0m, calc.TetoDeEntrada(1));
    Assert.Equal(1m, calc.ExcessoEmMontagem(2));   // 2 - 1 x 1: o segundo pronto passa do que o pai precisa
    Assert.Equal(0m, calc.ExcessoEmMontagem(3));
    Assert.Equal(0m, calc.Precisa(2));             // 1 x 1 - (2 - 1)
    Assert.Equal(1m, calc.Precisa(3));             // 1 x 1 - 0: o complemento do conjunto que vale
  }

  [Fact]
  public void Retirada_calcula_a_sobra_do_depois()
  {
    // Tres conjuntos inteiros a espera. O plano tira uma unidade do filho 2 da Solda e uma Peca de "a iniciar": o que
    // sobra e o resto do conjunto quebrado, como em `Conjunto_quebrado_vira_sobra_e_nao_ocupa_vaga`.
    var calc = Calcular(Base(), [.. Espera(2, Solda, 6m), .. Espera(3, Solda, 3m)]);
    var retirada = new Retirada((id, local) =>
        (id, local) == (2, Local.AguardandoMontagem(Solda)) || (id, local) == (1, Local.AIniciar) ? 1m : 0m);

    Assert.Equal(0m, calc.ExcessoEmMontagem(2));
    Assert.Equal(1m, calc.ExcessoEmMontagem(2, retirada));
    Assert.Equal(1m, calc.ExcessoEmMontagem(3, retirada));
  }

  [Fact]
  public void Retirada_no_pai_avulso_usa_a_formula_de_sempre()
  {
    // Avulso de 10 com 20 do filho 2 a espera: nada sobra. Tirada uma Peca de "a iniciar", o pai precisa de 18: sobram 2.
    var calc = Calcular(Base(kit: false), Espera(2, Solda, 20m));
    var retirada = new Retirada((id, local) => (id, local) == (1, Local.AIniciar) ? 1m : 0m);

    Assert.Equal(0m, calc.ExcessoEmMontagem(2));
    Assert.Equal(2m, calc.ExcessoEmMontagem(2, retirada));
  }
}

using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// Regra 25 na entrega (spec da Fase 3B, secao 4.3). Cenario Kit: Peca 1 de 10 que comeca na Solda (UtilizaKit);
/// Item 2 de 40 (razao 4) que termina no Corte; Item 3 de 10 (razao 1) que termina na Dobra.
///
/// Os arranjos seguem o fluxo real no livro (Inicio, Termino, Entrega), para nenhuma posicao ficar negativa: uma
/// coleta negativa faria a entrega cair em `SaldoInsuficiente` e o teste passar ou falhar pelo motivo errado.
/// </summary>
public class ConjuntoCompletoNaEntregaTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  private static CenarioDeExecucao Cenario(bool kit = true, int setorDoPai = Solda, int ultimoDo2 = Corte)
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, setorDoPai);
    c.No(2, 1, 40m, 4m, ultimoDo2);
    c.No(3, 1, 10m, 1m, Dobra);
    if (kit) c.Kit();
    return c;
  }

  /// <summary>Inicia e termina `quantidade` no passo 1 (no `setor`): fica aguardando coleta ali.</summary>
  private static void Pronto(CenarioDeExecucao c, int item, int setor, decimal quantidade)
  {
    c.Mover(item, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(setor, 1), quantidade);
    c.Mover(item, TiposDeMovimentacao.Termino, Local.NoSetor(setor, 1), Local.AguardandoColeta(setor, 1), quantidade);
  }

  /// <summary>Como <see cref="Pronto"/>, e depois entregue para aguardar montagem em `destino`.</summary>
  private static void AguardandoMontagemEm(CenarioDeExecucao c, int item, int setor, int destino, decimal quantidade)
  {
    Pronto(c, item, setor, quantidade);
    c.Mover(item, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(setor, 1), Local.AguardandoMontagem(destino),
        quantidade, Movimentador);
  }

  private static ItemDaEntregaDto DaColeta(int item, int setor, decimal quantidade) =>
      new(item, new OrigemDaEntregaDto(Posicoes.AguardandoColeta, setor, 1), null, quantidade);

  private static ItemDaEntregaDto DaMontagem(int item, int setor, decimal quantidade) =>
      new(item, new OrigemDaEntregaDto(Posicoes.AguardandoMontagem, setor, null), null, quantidade);

  private static Task<Result<IReadOnlyList<MovimentacaoDto>>> Entregar(CenarioDeExecucao c, params ItemDaEntregaDto[] itens) =>
      c.Entrega().Entregar(new EntregaDto(itens), Movimentador, Ct);

  private static void AfirmarRecusa(Result<IReadOnlyList<MovimentacaoDto>> r, string codigo, TipoDeErro tipo, CenarioDeExecucao c, int movimentosAntes)
  {
    Assert.False(r.Sucesso);
    Assert.Equal(codigo, r.Erro);
    Assert.Equal(tipo, r.TipoDoErro);
    Assert.Equal(movimentosAntes, c.Execucao.Movimentacoes.Count);
  }

  [Fact]
  public async Task Conjunto_completo_entra()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);
    Pronto(c, 3, Dobra, 2m);

    var r = await Entregar(c, DaColeta(2, Corte, 8m), DaColeta(3, Dobra, 2m));

    Assert.True(r.Sucesso);
    Assert.All(r.Valor!, m => Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Solda, "Solda", null), m.Destino));
  }

  [Fact]
  public async Task Filho_faltando_da_ConjuntoIncompleto()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await Entregar(c, DaColeta(2, Corte, 8m));

    AfirmarRecusa(r, CodigosDaExecucao.ConjuntoIncompleto, TipoDeErro.Validacao, c, antes);
    Assert.Contains("Falta No 3 no conjunto de No 1", r.Detalhe);
  }

  [Fact]
  public async Task Quantidade_que_nao_fecha_conjunto_da_ConjuntoIncompleto()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);
    Pronto(c, 3, Dobra, 2m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await Entregar(c, DaColeta(2, Corte, 6m), DaColeta(3, Dobra, 2m));   // 6 / 4 = 1,5

    AfirmarRecusa(r, CodigosDaExecucao.ConjuntoIncompleto, TipoDeErro.Validacao, c, antes);
    Assert.Contains("6 de No 2 não fecha conjuntos completos de No 1", r.Detalhe);
  }

  [Fact]
  public async Task Mesma_fracao_de_conjunto_em_todos_os_filhos_da_ConjuntoIncompleto()
  {
    // 6 / 4 = 1,5 e 1,5 / 1 = 1,5: o numero e o mesmo nos dois filhos, entao so a exigencia de inteiro recusa.
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);
    Pronto(c, 3, Dobra, 2m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await Entregar(c, DaColeta(2, Corte, 6m), DaColeta(3, Dobra, 1.5m));

    AfirmarRecusa(r, CodigosDaExecucao.ConjuntoIncompleto, TipoDeErro.Validacao, c, antes);
  }

  [Fact]
  public async Task Numeros_de_conjuntos_diferentes_da_ConjuntoIncompleto()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);
    Pronto(c, 3, Dobra, 3m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await Entregar(c, DaColeta(2, Corte, 8m), DaColeta(3, Dobra, 3m));   // 2 e 3 conjuntos

    AfirmarRecusa(r, CodigosDaExecucao.ConjuntoIncompleto, TipoDeErro.Validacao, c, antes);
  }

  [Fact]
  public async Task Alem_do_que_o_pai_precisa_da_AlemDoQueOPaiPrecisa()
  {
    // Os filhos tem mais que 10 x razao (sobra de refugo, legitima pela regra 26): 9 conjuntos ja esperam na Solda,
    // e ainda ha 2 conjuntos prontos. Com os filhos na conta exata, o teto nunca seria ultrapassado.
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 44m, 4m, Corte);
    c.No(3, 1, 11m, 1m, Dobra);
    c.Kit();
    AguardandoMontagemEm(c, 2, Corte, Solda, 36m);
    AguardandoMontagemEm(c, 3, Dobra, Solda, 9m);
    Pronto(c, 2, Corte, 8m);
    Pronto(c, 3, Dobra, 2m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await Entregar(c, DaColeta(2, Corte, 8m), DaColeta(3, Dobra, 2m));   // teto 1, leva 2

    AfirmarRecusa(r, CodigosDaExecucao.AlemDoQueOPaiPrecisa, TipoDeErro.Conflito, c, antes);
    Assert.Contains("só precisa receber 1 conjunto", r.Detalhe);
  }

  [Fact]
  public async Task Lista_mista_com_item_solto_entra_inteira()
  {
    var c = Cenario();
    c.NoDoAgrupamento(AgrupamentoId2, 10, null, 5m, null, Corte, Pintura);
    Pronto(c, 2, Corte, 8m);
    Pronto(c, 3, Dobra, 2m);
    Pronto(c, 10, Corte, 5m);

    var r = await Entregar(c, DaColeta(2, Corte, 8m), DaColeta(10, Corte, 5m), DaColeta(3, Dobra, 2m));

    Assert.True(r.Sucesso);
    Assert.Equal(3, r.Valor!.Count);
  }

  [Fact]
  public async Task Avulso_nao_exige_conjunto()
  {
    var c = Cenario(kit: false);
    Pronto(c, 2, Corte, 8m);

    Assert.True((await Entregar(c, DaColeta(2, Corte, 8m))).Sucesso);
  }

  [Fact]
  public async Task Pai_que_comeca_em_Setor_sem_UtilizaKit_nao_exige_conjunto()
  {
    var c = Cenario(setorDoPai: Pintura);
    Pronto(c, 2, Corte, 8m);

    Assert.True((await Entregar(c, DaColeta(2, Corte, 8m))).Sucesso);
  }

  [Fact]
  public async Task Folha_de_Kit_que_vai_ao_proximo_passo_nao_e_conjunto()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 40m, 4m, Corte, Solda);   // passa pela Solda no proprio Roteiro
    c.No(3, 1, 10m, 1m, Dobra);
    c.Kit();
    Pronto(c, 2, Corte, 4m);

    var r = await Entregar(c, DaColeta(2, Corte, 4m));

    Assert.True(r.Sucesso);
    Assert.Equal(new LocalDto(Posicoes.NoSetor, Solda, "Solda", 2), Assert.Single(r.Valor!).Destino);
  }

  [Fact]
  public async Task Filho_que_termina_na_Solda_entra_no_conjunto_com_os_irmaos()
  {
    var c = Cenario(ultimoDo2: Solda);
    Pronto(c, 2, Solda, 4m);
    Pronto(c, 3, Dobra, 1m);

    var r = await Entregar(c, DaColeta(2, Solda, 4m), DaColeta(3, Dobra, 1m));

    Assert.True(r.Sucesso);
  }

  [Fact]
  public async Task Redirecionamento_de_Kit_conta_a_espera_uma_vez_so()
  {
    // A Dobra tambem tem UtilizaKit e era o primeiro passo do pai; o PCP o mudou para a Solda. A Peca tem 2, e os 2
    // conjuntos que esperam na Dobra ocupam o teto inteiro (2 - 0 - 2 = 0); sem tira-los da conta, a entrega que
    // os leva a Solda seria recusada como alem do necessario.
    var c = new CenarioDeExecucao();
    c.No(1, null, 2m, null, Solda);
    c.No(2, 1, 8m, 4m, Corte);
    c.No(3, 1, 2m, 1m, Corte);
    c.Kit();
    c.Execucao.SetoresComKit.Add(Dobra);
    AguardandoMontagemEm(c, 2, Corte, Dobra, 8m);
    AguardandoMontagemEm(c, 3, Corte, Dobra, 2m);

    var r = await Entregar(c, DaMontagem(2, Dobra, 8m), DaMontagem(3, Dobra, 2m));

    Assert.True(r.Sucesso);
  }

  [Fact]
  public async Task Redirecionamento_de_Kit_incompleto_da_ConjuntoIncompleto()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 2m, null, Solda);
    c.No(2, 1, 8m, 4m, Corte);
    c.No(3, 1, 2m, 1m, Corte);
    c.Kit();
    AguardandoMontagemEm(c, 2, Corte, Pintura, 8m);
    AguardandoMontagemEm(c, 3, Corte, Pintura, 2m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await Entregar(c, DaMontagem(2, Pintura, 8m));

    AfirmarRecusa(r, CodigosDaExecucao.ConjuntoIncompleto, TipoDeErro.Validacao, c, antes);
  }
}

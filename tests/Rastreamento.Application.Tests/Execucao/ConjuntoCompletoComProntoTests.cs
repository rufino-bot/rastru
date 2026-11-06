using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// A regra 25 na entrega com o conjunto em formacao (D8 da spec da Fase 5B-2; decisao P4 do plano de backend). Cenario
/// Kit: Peca 1 que comeca na Solda (`UtilizaKit`); A (2) termina no Corte e B (3) na Dobra, os dois de razao 1. A chega
/// a Solda por um `Pronto` (o Retrabalho) ou por uma `Entrega` (o Pedido de fabricacao); B fica pronto na Dobra.
/// </summary>
public class ConjuntoCompletoComProntoTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;
  private const int A = 2, B = 3;

  private static CenarioDeExecucao Cenario(decimal quantidadeDoPai)
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, quantidadeDoPai, null, Solda);
    c.No(A, 1, quantidadeDoPai, 1m, Corte);
    c.No(B, 1, quantidadeDoPai, 1m, Dobra);
    c.Kit();
    return c;
  }

  /// <summary>O `Pronto` do Retrabalho: de "a iniciar" direto para a montagem na Solda, apontando a baixa.</summary>
  private static void ProntoNaSolda(CenarioDeExecucao c, int item, decimal quantidade) =>
      c.Mover(item, TiposDeMovimentacao.Pronto, Local.AIniciar, Local.AguardandoMontagem(Solda), quantidade).PerdaId = 900;

  /// <summary>O caminho da fabricacao: inicia, termina no passo 1 e e entregue a Solda.</summary>
  private static void EntregueNaSolda(CenarioDeExecucao c, int item, int setor, decimal quantidade)
  {
    NaColeta(c, item, setor, quantidade);
    c.Mover(item, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(setor, 1), Local.AguardandoMontagem(Solda),
        quantidade, Movimentador);
  }

  private static void NaColeta(CenarioDeExecucao c, int item, int setor, decimal quantidade)
  {
    c.Mover(item, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(setor, 1), quantidade);
    c.Mover(item, TiposDeMovimentacao.Termino, Local.NoSetor(setor, 1), Local.AguardandoColeta(setor, 1), quantidade);
  }

  private static ItemDaEntregaDto DaColeta(int item, int setor, decimal quantidade) =>
      new(item, new OrigemDaEntregaDto(Posicoes.AguardandoColeta, setor, 1), null, quantidade);

  private static Task<Result<IReadOnlyList<MovimentacaoDto>>> Entregar(CenarioDeExecucao c, params ItemDaEntregaDto[] itens) =>
      c.Entrega().Entregar(new EntregaDto(itens), Movimentador, Ct);

  [Fact]
  public async Task Entrega_que_completa_o_conjunto_em_formacao_passa()
  {
    var c = Cenario(1m);
    ProntoNaSolda(c, A, 1m);
    NaColeta(c, B, Dobra, 1m);

    var r = await Entregar(c, DaColeta(B, Dobra, 1m));

    Assert.True(r.Sucesso, r.Detalhe);
    var m = Assert.Single(r.Valor!);
    Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Solda, "Solda", null), m.Destino);
    Assert.Equal(1m, c.Calcular().AguardandoMontagem(B, Solda));
  }

  [Fact]
  public async Task Entrega_alem_do_complemento_exige_conjunto_completo()
  {
    var c = Cenario(2m);
    ProntoNaSolda(c, A, 1m);
    NaColeta(c, B, Dobra, 2m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await Entregar(c, DaColeta(B, Dobra, 2m));   // 1 completa o conjunto; o outro 1 vai sem A

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaExecucao.ConjuntoIncompleto, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Contains("Falta No 2 no conjunto de No 1", r.Detalhe);
    Assert.Equal(antes, c.Execucao.Movimentacoes.Count);
  }

  [Fact]
  public async Task Pedido_de_fabricacao_continua_exigindo_o_conjunto()
  {
    var c = Cenario(1m);
    EntregueNaSolda(c, A, Corte, 1m);
    NaColeta(c, B, Dobra, 1m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await Entregar(c, DaColeta(B, Dobra, 1m));

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaExecucao.ConjuntoIncompleto, r.Erro);
    Assert.Contains("Falta No 2 no conjunto de No 1", r.Detalhe);
    Assert.Equal(antes, c.Execucao.Movimentacoes.Count);
  }

  /// <summary>
  /// Pai de 2 que perdeu 1 de "a iniciar" depois do pronto (a baixa gravada direto no livro): so precisa de 1 conjunto.
  /// A tem 2 prontos na Solda e B tem 2 aguardando coleta.
  /// </summary>
  private static CenarioDeExecucao PaiQuePerdeuDepoisDoPronto()
  {
    var c = Cenario(2m);
    ProntoNaSolda(c, A, 2m);
    c.Mover(1, TiposDeMovimentacao.Perda, Local.AIniciar, Local.Perdido, 1m).PerdaId = 901;
    NaColeta(c, B, Dobra, 2m);
    return c;
  }

  [Fact]
  public async Task Complemento_nao_passa_do_que_o_pai_ainda_precisa()
  {
    var c = PaiQuePerdeuDepoisDoPronto();
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await Entregar(c, DaColeta(B, Dobra, 2m));   // 1 completa o unico conjunto que vale; o outro 1 vai sem A

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaExecucao.ConjuntoIncompleto, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Contains("Falta No 2 no conjunto de No 1", r.Detalhe);
    Assert.Equal(antes, c.Execucao.Movimentacoes.Count);
  }

  [Fact]
  public async Task Complemento_do_unico_conjunto_que_vale_passa()
  {
    var c = PaiQuePerdeuDepoisDoPronto();

    var r = await Entregar(c, DaColeta(B, Dobra, 1m));

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(1m, Assert.Single(r.Valor!).Quantidade);
    Assert.Equal(1m, c.Calcular().AguardandoMontagem(B, Solda));
    Assert.Equal(1m, c.Calcular().Saldo(B, Local.AguardandoColeta(Dobra, 1)));
  }

  [Fact]
  public async Task Pronto_estornado_nao_forma_conjunto()
  {
    // O estorno do `Pronto` devolve A a "a iniciar": nada esta em formacao, e B sozinho volta a ser incompleto.
    var c = Cenario(1m);
    var pronto = c.Mover(A, TiposDeMovimentacao.Pronto, Local.AIniciar, Local.AguardandoMontagem(Solda), 1m);
    pronto.PerdaId = 900;
    var estorno = c.Mover(A, TiposDeMovimentacao.Estorno, Local.AguardandoMontagem(Solda), Local.AIniciar, 1m);
    estorno.PerdaId = 900;
    estorno.EstornoDeId = pronto.Id;
    NaColeta(c, B, Dobra, 1m);

    var r = await Entregar(c, DaColeta(B, Dobra, 1m));

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaExecucao.ConjuntoIncompleto, r.Erro);
  }
}

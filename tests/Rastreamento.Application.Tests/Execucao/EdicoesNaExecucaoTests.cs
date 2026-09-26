using Rastreamento.Application.Common;
using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// As edicoes da Fase 2 que a Fase 3 passa a validar contra o livro (spec secao 4.7) e a pôr no esquema
/// de trava (secao 8.1; o DELETE trava a subarvore inteira — desvio D5 do plano 2). Os nos do cenario
/// nao tem Componente, entao a edicao manda a descricao (regra 19), senao seria recusada por ela.
/// </summary>
public class EdicoesNaExecucaoTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  [Fact]
  public async Task Reduzir_abaixo_do_que_saiu_de_a_iniciar_da_QuantidadeAbaixoDoMovimentado()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);

    var r = await c.Estrutura().EditarNo(1, new EdicaoDeNoDto("No 1", 5m), Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaExecucao.QuantidadeAbaixoDoMovimentado, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains("6", r.Detalhe);
    Assert.Equal(10m, c.Estruturas.Itens.Single(i => i.Id == 1).Quantidade);
  }

  [Fact]
  public async Task Reduzir_ate_o_que_ja_andou_e_permitido_e_trava_o_no()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);

    var r = await c.Estrutura().EditarNo(1, new EdicaoDeNoDto("No 1", 6m), Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(6m, c.Estruturas.Itens.Single(i => i.Id == 1).Quantidade);
    Assert.Equal(new[] { 1 }, Assert.Single(c.Execucao.Travas));
  }

  [Fact]
  public async Task Pai_nao_desce_abaixo_do_total_montado()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 10m, 1m, Corte);
    c.Execucao.Montagens.Add(new Montagem
    {
      Id = 900, EstruturaItemId = 1, SetorId = Solda, Quantidade = 4m, DataHora = DateTime.UtcNow, UsuarioId = Operador,
    });

    var r = await c.Estrutura().EditarNo(1, new EdicaoDeNoDto("No 1", 3m), Ct);

    Assert.Equal(CodigosDaExecucao.QuantidadeAbaixoDoMovimentado, r.Erro);
    Assert.Contains("4", r.Detalhe);
  }

  [Fact]
  public async Task Conflito_na_edicao_vira_ConflitoDeConcorrencia()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.Execucao.ConflitoNaProximaTransacao = true;

    var r = await c.Estrutura().EditarNo(1, new EdicaoDeNoDto("No 1", 8m), Ct);

    Assert.Equal(CodigosDaExecucao.ConflitoDeConcorrencia, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
  }

  [Fact]
  public async Task Excluir_trava_a_subarvore_inteira_antes_de_ler_o_status()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.No(2, 1, 10m, 1m, Corte);
    c.No(3, 2, 10m, 1m, Corte);

    var r = await c.Estrutura().ExcluirNo(1, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(new[] { 1, 2, 3 }, Assert.Single(c.Execucao.Travas).Order().ToArray());
    Assert.Empty(c.Estruturas.Itens);
  }

  [Fact]
  public async Task Excluir_em_Pedido_em_producao_continua_PedidoNaoAberto()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.Execucao.StatusDoPedido[PedidoId] = "EmProducao";

    var r = await c.Estrutura().ExcluirNo(1, Ct);

    Assert.Equal("PedidoNaoAberto", r.Erro);
    Assert.Single(c.Estruturas.Itens);
  }
}

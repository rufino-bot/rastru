using Rastreamento.Application.Baixa;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Baixa;

/// <summary>
/// Quem a baixa trava, e quando (secao 5.1 da spec da Fase 5B-1; spec da Fase 3, secao 8.1). O resultado das baixas
/// simultaneas e afirmado nos testes de Api, contra o banco; la o SERIALIZABLE com a retentativa de deadlock chega ao
/// mesmo resultado sem as travas, so mais devagar. Aqui a ordem fica afirmada sem depender de tempo: a arvore inteira da
/// Peca do topo, em ordem crescente de Id, depois o Pedido, tudo antes da primeira gravacao; e a previa nao trava nada
/// (decisao P4 do plano de backend da Fase 5B-1).
///
/// A arvore tem tres niveis e dois ramos: Peca 1, filho 2 com neto 3, e filho 4, que so se alcanca pela arvore da Peca.
/// </summary>
public class BaixaUseCaseTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  private static (CenarioDeExecucao C, BaixaUseCase Baixa) Cenario()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 20m, 2m, Corte);
    c.No(3, 2, 20m, 1m, Dobra);
    c.No(4, 1, 10m, 1m, Dobra);
    return (c, new BaixaUseCase(c.Execucao, new FakeBaixaRepo(c.Execucao, c.Estruturas), c.Estruturas, c.Catalogo));
  }

  private static NovaBaixaDto UmDoNetoAIniciar() =>
      new(MotivosDePerda.PerdaArmazem, null, false, [new OrigemDto(Posicoes.AIniciar, null, null, 1m)], false);

  [Fact]
  public async Task Baixa_trava_a_arvore_da_Peca_do_topo_em_ordem_e_depois_o_Pedido_antes_de_gravar()
  {
    var (c, baixa) = Cenario();

    var r = await baixa.DarBaixa(3, UmDoNetoAIniciar(), Qualidade, Ct);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(new[] { "nos:1,2,3,4", "pedidos:3", "save" }, c.Execucao.Eventos.Take(3));
    // O Pedido e lido depois da trava dele: lido antes, a trava converteria S em U e deadlockaria (`PK_Pedido`).
    Assert.True(c.Execucao.PedidoLidoAposEventos[0] >= 2);
  }

  [Fact]
  public async Task Previa_nao_trava_nem_grava()
  {
    var (c, baixa) = Cenario();

    var r = await baixa.PreverBaixa(3, UmDoNetoAIniciar(), Ct);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Empty(c.Execucao.Eventos);
    Assert.Equal(0, c.Execucao.Transacoes);
  }
}

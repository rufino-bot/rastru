using Rastreamento.Application.Execucao;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Expedicao;

/// <summary>
/// O fechamento recalculado pelo livro que ja existe (C11 e C13 da spec da Fase 5A, decisao P4 do plano de
/// backend da Fase 5A): a entrega e o estorno generico recalculam o status do Pedido quando o movimento toca o
/// fim do caminho, e so travam o Pedido quando algum no da lista e Peca. Cenario: Pedido `EmProducao`, Peca 1
/// de 4 com Roteiro de um passo (Corte), as 4 terminadas e aguardando coleta.
/// </summary>
public class FechamentoDoPedidoTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  private static CenarioDeExecucao Cenario()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 4m, null, Corte);
    c.Execucao.StatusDoPedido[PedidoId] = "EmProducao";
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 4m);
    c.Mover(1, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 4m);
    return c;
  }

  private static EntregaDto DaColeta(int no, decimal quantidade) =>
      new([new ItemDaEntregaDto(no, new OrigemDaEntregaDto(Posicoes.AguardandoColeta, Corte, 1), null, quantidade)]);

  [Fact]
  public async Task Entrega_que_leva_a_ultima_unidade_ao_local_de_expedicao_poe_AguardandoExpedicao()
  {
    var c = Cenario();

    var parcial = await c.Entrega().Entregar(DaColeta(1, 3m), Movimentador, Ct);
    Assert.True(parcial.Sucesso);
    Assert.Equal("EmProducao", c.Execucao.StatusDoPedido[PedidoId]);   // falta uma unidade (C11)

    var ultima = await c.Entrega().Entregar(DaColeta(1, 1m), Movimentador, Ct);

    Assert.True(ultima.Sucesso);
    Assert.Equal("AguardandoExpedicao", c.Execucao.StatusDoPedido[PedidoId]);
    Assert.Null(c.Execucao.DataConclusaoDoPedido.GetValueOrDefault(PedidoId));   // nada expedido: nao conclui
  }

  [Fact]
  public async Task Estorno_dessa_entrega_volta_a_EmProducao()
  {
    var c = Cenario();
    var entrega = await c.Entrega().Entregar(DaColeta(1, 4m), Movimentador, Ct);
    Assert.Equal("AguardandoExpedicao", c.Execucao.StatusDoPedido[PedidoId]);

    var r = await c.Estorno().EstornarMovimentacao(entrega.Valor!.Single().Id, Movimentador, false, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal("EmProducao", c.Execucao.StatusDoPedido[PedidoId]);
    Assert.Equal(4m, c.Calcular().Saldo(1, Local.AguardandoColeta(Corte, 1)));
  }

  [Fact]
  public async Task Entrega_de_Item_para_montagem_nao_trava_Pedido()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 4m, null, Solda);
    c.No(2, 1, 8m, 2m, Corte);
    c.Execucao.StatusDoPedido[PedidoId] = "EmProducao";
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 8m);

    var r = await c.Entrega().Entregar(DaColeta(2, 8m), Movimentador, Ct);

    Assert.True(r.Sucesso);
    Assert.Empty(c.Execucao.TravasDePedido);   // P4: so Item na lista, nenhum Pedido travado
    Assert.Equal("EmProducao", c.Execucao.StatusDoPedido[PedidoId]);
  }

  [Fact]
  public async Task Entrega_de_Peca_trava_o_Pedido_depois_dos_nos_e_antes_de_gravar()
  {
    var c = Cenario();

    var r = await c.Entrega().Entregar(DaColeta(1, 4m), Movimentador, Ct);

    Assert.True(r.Sucesso);
    // A ordem fixa da spec da Fase 3, secao 8.1: nos, depois Pedidos, e so entao a primeira escrita.
    Assert.Equal(new[] { "nos:1", "pedidos:1", "save" }, c.Execucao.Eventos);
  }

  [Fact]
  public async Task Estorno_de_movimento_de_Item_nao_trava_Pedido()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 4m, null, Solda);
    c.No(2, 1, 8m, 2m, Corte);
    c.Execucao.StatusDoPedido[PedidoId] = "EmProducao";
    var termino = c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 8m);

    var r = await c.Estorno().EstornarMovimentacao(termino.Id, Operador, false, Ct);

    Assert.True(r.Sucesso);
    Assert.Empty(c.Execucao.TravasDePedido);
  }

  [Fact]
  public async Task Estorno_de_movimento_de_Peca_trava_o_Pedido_depois_do_no_e_antes_de_gravar()
  {
    var c = Cenario();
    var entrega = await c.Entrega().Entregar(DaColeta(1, 4m), Movimentador, Ct);
    c.Execucao.Eventos.Clear();

    var r = await c.Estorno().EstornarMovimentacao(entrega.Valor!.Single().Id, Movimentador, false, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(new[] { "nos:1", "pedidos:1", "save" }, c.Execucao.Eventos);
  }
}

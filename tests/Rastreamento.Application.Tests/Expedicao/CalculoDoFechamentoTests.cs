using Rastreamento.Application.Expedicao;
using Rastreamento.Domain.Abstractions;
using Xunit;

namespace Rastreamento.Application.Tests.Expedicao;

/// <summary>
/// O status do Pedido e o fechamento pela regra 13, puros (C11, C12, C13 e C16 da spec da Fase 5A): a Peca
/// conclui com tudo expedido, o Agrupamento com a ultima Peca, o Pedido com o ultimo Agrupamento que tem
/// Peca; `AguardandoExpedicao` quando todo o nao expedido esta no local de expedicao.
/// </summary>
public class CalculoDoFechamentoTests
{
  private const int Pedido = 7, AgrA = 70, AgrB = 71;

  private static PecaNoFechamento Peca(int id, int agrupamento, decimal quantidade, decimal expedido, decimal noLocal) =>
      new(id, agrupamento, quantidade, expedido, noLocal);

  [Fact]
  public void Tudo_expedido_conclui_Pedido_e_Agrupamentos()
  {
    var f = CalculoDoFechamento.Calcular(Pedido, "AguardandoExpedicao",
        [Peca(1, AgrA, 4, 4, 0), Peca(2, AgrA, 2, 2, 0), Peca(3, AgrB, 5, 5, 0)]);

    Assert.Equal(Pedido, f.PedidoId);
    Assert.Equal("Concluido", f.Status);
    Assert.True(f.PedidoConcluido);
    Assert.Equal([new AgrupamentoNoFechamento(AgrA, true), new AgrupamentoNoFechamento(AgrB, true)], f.Agrupamentos);
  }

  [Fact]
  public void Tudo_que_falta_no_local_de_expedicao_e_AguardandoExpedicao()
  {
    // Uma Peca ja toda expedida e outra com o que falta (3) inteiro no local de expedicao.
    var f = CalculoDoFechamento.Calcular(Pedido, "EmProducao",
        [Peca(1, AgrA, 4, 4, 0), Peca(2, AgrB, 5, 2, 3)]);

    Assert.Equal("AguardandoExpedicao", f.Status);
    Assert.False(f.PedidoConcluido);
    Assert.Equal([new AgrupamentoNoFechamento(AgrA, true), new AgrupamentoNoFechamento(AgrB, false)], f.Agrupamentos);
  }

  [Fact]
  public void Uma_unidade_fora_do_local_de_expedicao_e_EmProducao()
  {
    // Peca de 5: 2 expedidas, 2 no local de expedicao e 1 em conserto (que conta como em producao).
    var f = CalculoDoFechamento.Calcular(Pedido, "AguardandoExpedicao",
        [Peca(1, AgrA, 4, 0, 4), Peca(2, AgrA, 5, 2, 2)]);

    Assert.Equal("EmProducao", f.Status);
    Assert.False(f.PedidoConcluido);
    Assert.Equal([new AgrupamentoNoFechamento(AgrA, false)], f.Agrupamentos);
  }

  [Fact]
  public void Agrupamento_concluido_com_outro_aberto_nao_conclui_o_Pedido()
  {
    var f = CalculoDoFechamento.Calcular(Pedido, "EmProducao",
        [Peca(1, AgrA, 4, 4, 0), Peca(2, AgrB, 3, 0, 0)]);

    Assert.Equal("EmProducao", f.Status);
    Assert.False(f.PedidoConcluido);
    Assert.Equal([new AgrupamentoNoFechamento(AgrA, true), new AgrupamentoNoFechamento(AgrB, false)], f.Agrupamentos);
  }

  [Fact]
  public void Agrupamento_concluido_so_com_todas_as_Pecas()
  {
    // Uma Peca do Agrupamento concluida nao basta.
    var f = CalculoDoFechamento.Calcular(Pedido, "AguardandoExpedicao",
        [Peca(1, AgrA, 4, 4, 0), Peca(2, AgrA, 3, 1, 2)]);

    Assert.Equal([new AgrupamentoNoFechamento(AgrA, false)], f.Agrupamentos);
    Assert.Equal("AguardandoExpedicao", f.Status);
  }

  [Fact]
  public void Agrupamento_sem_Peca_nao_segura_o_Pedido()
  {
    // C16: o AgrB existe no Pedido, mas sem Peca nao aparece em `pecas`; o Pedido conclui sem ele.
    var f = CalculoDoFechamento.Calcular(Pedido, "AguardandoExpedicao", [Peca(1, AgrA, 4, 4, 0)]);

    Assert.Equal("Concluido", f.Status);
    Assert.True(f.PedidoConcluido);
    Assert.Equal([new AgrupamentoNoFechamento(AgrA, true)], f.Agrupamentos);
  }

  [Fact]
  public void Pedido_sem_Peca_nenhuma_nao_muda()
  {
    var f = CalculoDoFechamento.Calcular(Pedido, "EmProducao", []);

    Assert.Equal(Pedido, f.PedidoId);
    Assert.Equal("EmProducao", f.Status);
    Assert.False(f.PedidoConcluido);
    Assert.Empty(f.Agrupamentos);
  }

  [Theory]
  [InlineData("Aberto")]
  [InlineData("Cancelado")]
  public void Status_Aberto_e_Cancelado_nao_mudam(string status)
  {
    // Peca toda expedida: com outro status, concluiria. Aqui nada muda, nem os Agrupamentos.
    var f = CalculoDoFechamento.Calcular(Pedido, status, [Peca(1, AgrA, 4, 4, 0)]);

    Assert.Equal(status, f.Status);
    Assert.False(f.PedidoConcluido);
    Assert.Empty(f.Agrupamentos);
  }

  [Fact]
  public void Concluido_que_perdeu_uma_remessa_volta_a_AguardandoExpedicao()
  {
    // C13: o estorno da remessa devolveu 2 ao local de expedicao.
    var f = CalculoDoFechamento.Calcular(Pedido, "Concluido",
        [Peca(1, AgrA, 4, 4, 0), Peca(2, AgrB, 5, 3, 2)]);

    Assert.Equal("AguardandoExpedicao", f.Status);
    Assert.False(f.PedidoConcluido);
    Assert.Equal([new AgrupamentoNoFechamento(AgrA, true), new AgrupamentoNoFechamento(AgrB, false)], f.Agrupamentos);
  }

  [Fact]
  public void Concluido_que_continua_todo_expedido_continua_Concluido()
  {
    var f = CalculoDoFechamento.Calcular(Pedido, "Concluido", [Peca(1, AgrA, 4, 4, 0)]);

    Assert.Equal("Concluido", f.Status);
    Assert.True(f.PedidoConcluido);
  }
}

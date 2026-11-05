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

  [Fact]
  public async Task Descartar_o_Pedido_trava_a_uniao_das_arvores_em_ordem_de_Id_e_depois_o_Pedido_antes_de_gravar()
  {
    // Duas Pecas cujas arvores se entrelacam por Id: a primeira tem os nos 1, 3 e 4; a segunda, 2 e 5. Uma
    // concatenacao arvore a arvore daria 1,3,4,2,5; a ordem pedida e a da uniao, crescente de Id.
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(3, 1, 20m, 2m, Corte);
    c.No(4, 1, 10m, 1m, Dobra);
    c.No(2, null, 3m, null, Solda);
    c.No(5, 2, 3m, 1m, Corte);
    var baixa = new BaixaUseCase(c.Execucao, new FakeBaixaRepo(c.Execucao, c.Estruturas), c.Estruturas, c.Catalogo);

    var r = await baixa.DescartarPedido(PedidoId, new DescarteDto(null), Qualidade, Ct);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(new[] { "nos:1,2,3,4,5", "pedidos:1,2", "save" }, c.Execucao.Eventos.Take(3));
    // O Pedido e lido depois da trava dele (a ordem que evita o deadlock em `PK_Pedido`).
    Assert.True(c.Execucao.PedidoLidoAposEventos[0] >= 2);
  }

  [Fact]
  public async Task Estorno_trava_os_nos_da_baixa_em_ordem_e_depois_o_Pedido_antes_de_gravar()
  {
    // A baixa da Peca (1) alcanca 1, 2, 3 e 4 pela cascata. O estorno
    // trava so os nos que tem linha, em ordem crescente de Id, e depois o Pedido.
    var (c, baixa) = Cenario();
    var dada = await baixa.DarBaixa(
        1, new NovaBaixaDto(MotivosDePerda.PerdaArmazem, null, false, [new OrigemDto(Posicoes.AIniciar, null, null, 1m)], false),
        Qualidade, Ct);
    Assert.True(dada.Sucesso, dada.Detalhe);
    c.Execucao.Eventos.Clear();

    var r = await baixa.Estornar(dada.Valor!.Id, Qualidade, false, Ct);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(new[] { "nos:1,2,3,4", "pedidos:1,2,3,4", "save" }, c.Execucao.Eventos.Take(3));
  }

  [Fact]
  public async Task Previa_do_descarte_nao_trava_nem_grava()
  {
    var (c, baixa) = Cenario();

    var r = await baixa.PreverDescarteDoPedido(PedidoId, Ct);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Empty(c.Execucao.Eventos);
    Assert.Equal(0, c.Execucao.Transacoes);
  }
}

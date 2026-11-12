using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// A Fila do Setor e as Tarefas saem na ordem da data de entrega do Pedido do nó, a mais próxima primeiro, e o `Id` do
/// nó desempata. Todo cenário aqui cria os nós do Pedido de prazo MAIOR antes dos do de prazo MENOR: a ordem de `Id`
/// que existia antes da regra os poria na ordem errada, então nenhum teste passa por acaso.
/// </summary>
public class OrdemPorPrazoTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  private static readonly DateOnly Longe = new(2026, 12, 30);
  private static readonly DateOnly Perto = new(2026, 12, 10);

  /// <summary>O Pedido 1 (nós de `Id` baixo) entrega depois do Pedido 2 (nós de `Id` alto).</summary>
  private static CenarioDeExecucao Cenario()
  {
    var c = new CenarioDeExecucao();
    c.Execucao.DataDeEntregaDoPedido[PedidoId] = Longe;
    c.Execucao.DataDeEntregaDoPedido[PedidoId2] = Perto;
    return c;
  }

  private static void PedidoAvulso(CenarioDeExecucao c, int pedidoId, DateOnly prazo)
  {
    c.Execucao.Agrupamentos[pedidoId] = ($"AG-0{pedidoId}", pedidoId, $"PED-0{pedidoId}");
    c.Execucao.StatusDoPedido[pedidoId] = "Aberto";
    c.Execucao.DataDeEntregaDoPedido[pedidoId] = prazo;
  }

  private static void Pronto(CenarioDeExecucao c, int item, int setor, decimal quantidade) =>
      c.Mover(item, TiposDeMovimentacao.Termino, Local.NoSetor(setor, 1), Local.AguardandoColeta(setor, 1), quantidade);

  private static void EmEspera(CenarioDeExecucao c, int item, int setor, decimal quantidade)
  {
    Pronto(c, item, Corte, quantidade);
    c.Mover(item, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(setor), quantidade);
  }

  [Fact]
  public async Task A_iniciar_sai_pelo_prazo_do_Pedido_e_o_Id_do_no_desempata()
  {
    var c = Cenario();
    c.No(1, null, 10m, null, Corte);
    c.NoDoAgrupamento(AgrupamentoId2, 2, null, 10m, null, Corte);
    c.No(3, null, 10m, null, Corte);

    var aIniciar = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!.AIniciar;

    Assert.Equal(new[] { 2, 1, 3 }, aIniciar.Select(l => l.No.Id).ToArray());
  }

  [Fact]
  public async Task O_pausado_continua_no_fim_do_a_iniciar_e_cada_grupo_sai_pelo_prazo()
  {
    var c = new CenarioDeExecucao();
    c.Execucao.DataDeEntregaDoPedido[PedidoId] = new DateOnly(2026, 12, 30);    // pausado
    c.Execucao.DataDeEntregaDoPedido[PedidoId2] = new DateOnly(2026, 12, 25);
    PedidoAvulso(c, 3, new DateOnly(2026, 12, 20));                              // pausado
    PedidoAvulso(c, 4, new DateOnly(2026, 12, 10));
    c.No(1, null, 10m, null, Corte);
    c.NoDoAgrupamento(AgrupamentoId2, 2, null, 10m, null, Corte);
    c.NoDoAgrupamento(3, 3, null, 10m, null, Corte);
    c.NoDoAgrupamento(4, 4, null, 10m, null, Corte);
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto("parado"), Pcp, Ct);
    await c.Pausa().Pausar(3, new NovaPausaDto("parado"), Pcp, Ct);

    var aIniciar = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!.AIniciar;

    Assert.Equal(new[] { 4, 2, 3, 1 }, aIniciar.Select(l => l.No.Id).ToArray());
  }

  [Fact]
  public async Task Em_trabalho_e_aguardando_coleta_saem_pelo_prazo()
  {
    var c = Cenario();
    c.No(1, null, 10m, null, Corte, Dobra);
    c.NoDoAgrupamento(AgrupamentoId2, 2, null, 10m, null, Corte, Dobra);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);
    c.Mover(2, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);
    Pronto(c, 1, Corte, 2m);
    Pronto(c, 2, Corte, 2m);

    var fila = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!;

    Assert.Equal(new[] { 2, 1 }, fila.EmTrabalho.Select(l => l.No.Id).ToArray());
    Assert.Equal(new[] { 2, 1 }, fila.AguardandoColeta.Select(l => l.No.Id).ToArray());
  }

  [Fact]
  public async Task A_sobra_sai_pelo_prazo()
  {
    var c = Cenario();
    c.No(1, null, 10m, null, Corte);
    c.NoDoAgrupamento(AgrupamentoId2, 2, null, 10m, null, Corte);
    c.Execucao.StatusDoPedido[PedidoId] = "Concluido";
    c.Execucao.StatusDoPedido[PedidoId2] = "Concluido";
    Pronto(c, 1, Corte, 4m);
    Pronto(c, 2, Corte, 4m);

    var sobra = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!.Sobra;

    Assert.Equal(new[] { 2, 1 }, sobra.Select(l => l.No.Id).ToArray());
  }

  [Fact]
  public async Task A_sobra_do_mesmo_prazo_continua_pelo_no_e_pelo_passo()
  {
    var c = Cenario();
    c.Execucao.DataDeEntregaDoPedido[PedidoId2] = Longe;
    c.No(1, null, 10m, null, Corte, Dobra, Corte);
    c.NoDoAgrupamento(AgrupamentoId2, 2, null, 10m, null, Corte);
    c.Execucao.StatusDoPedido[PedidoId] = "Concluido";
    c.Execucao.StatusDoPedido[PedidoId2] = "Concluido";
    Pronto(c, 2, Corte, 4m);
    c.Mover(1, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 3), Local.AguardandoColeta(Corte, 3), 4m);
    Pronto(c, 1, Corte, 4m);

    var sobra = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!.Sobra;

    Assert.Equal(new[] { (1, (int?)1), (1, 3), (2, 1) }, sobra.Select(l => (l.No.Id, l.Ordem)).ToArray());
  }

  [Fact]
  public async Task O_conserto_sai_pelo_prazo_na_Fila_e_nas_Tarefas()
  {
    var c = Cenario();
    c.PecaNoConserto(1, 6m, Dobra, 3m);
    c.PecaNoConserto(2, 6m, Dobra, 3m);
    c.Estruturas.Itens.Single(i => i.Id == 2).AgrupamentoId = AgrupamentoId2;
    c.Execucao.StatusDoPedido[PedidoId2] = "EmProducao";
    c.Mover(1, TiposDeMovimentacao.Entrega, Local.AguardandoConserto(Dobra), Local.EmConserto(Dobra), 2m, Movimentador);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoConserto(Dobra), Local.EmConserto(Dobra), 2m, Movimentador);

    var fila = (await c.Consulta().Fila(Dobra, ComoOperador, Ct)).Valor!.Conserto;
    var tarefas = (await c.Consulta().Tarefas(Ct)).Valor!.Consertos;

    Assert.Equal(new[] { 2, 1 }, fila.Select(l => l.No.Id).ToArray());
    Assert.Equal(new[] { 2, 1 }, tarefas.Select(t => t.No.Id).ToArray());
  }

  [Fact]
  public async Task Aguardando_montagem_sai_pelo_prazo_do_Pedido_do_pai()
  {
    var c = Cenario();
    c.No(1, null, 5m, null, Solda);
    c.No(2, 1, 5m, 1m, Corte);
    c.NoDoAgrupamento(AgrupamentoId2, 3, null, 5m, null, Solda);
    c.NoDoAgrupamento(AgrupamentoId2, 4, 3, 5m, 1m, Corte);
    EmEspera(c, 2, Solda, 2m);
    EmEspera(c, 4, Solda, 2m);

    var montagem = (await c.Consulta().Fila(Solda, ComoOperador, Ct)).Valor!.AguardandoMontagem;

    Assert.Equal(new[] { 3, 1 }, montagem.Select(m => m.Pai.Id).ToArray());
  }

  [Fact]
  public async Task Os_itens_de_cada_grupo_das_Tarefas_saem_pelo_prazo()
  {
    var c = Cenario();
    c.No(1, null, 10m, null, Corte, Dobra);
    c.NoDoAgrupamento(AgrupamentoId2, 2, null, 10m, null, Corte, Dobra);
    c.No(3, null, 10m, null, Corte, Dobra);
    Pronto(c, 1, Corte, 4m);
    Pronto(c, 2, Corte, 4m);
    Pronto(c, 3, Corte, 4m);

    var grupo = Assert.Single((await c.Consulta().Tarefas(Ct)).Valor!.Grupos);

    Assert.Equal(new[] { 2, 1, 3 }, grupo.Itens.Select(t => t.No.Id).ToArray());
  }

  [Fact]
  public async Task Os_grupos_das_Tarefas_continuam_pelo_Setor_mesmo_com_o_prazo_mais_perto_no_de_Id_maior()
  {
    var c = Cenario();
    c.No(1, null, 10m, null, Corte, Pintura);
    c.NoDoAgrupamento(AgrupamentoId2, 2, null, 10m, null, Dobra, Pintura);
    Pronto(c, 1, Corte, 4m);
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Dobra, 1), Local.AguardandoColeta(Dobra, 1), 4m);

    var grupos = (await c.Consulta().Tarefas(Ct)).Valor!.Grupos;

    Assert.Equal(new[] { Corte, Dobra }, grupos.Select(g => g.SetorId).ToArray());
  }

  [Fact]
  public async Task Os_Kits_montaveis_saem_pelo_prazo_do_Pedido_do_pai()
  {
    var c = Cenario();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 10m, 1m, Corte);
    c.NoDoAgrupamento(AgrupamentoId2, 3, null, 10m, null, Solda);
    c.NoDoAgrupamento(AgrupamentoId2, 4, 3, 10m, 1m, Corte);
    c.Kit(AgrupamentoId);
    c.Kit(AgrupamentoId2);
    Pronto(c, 2, Corte, 10m);
    Pronto(c, 4, Corte, 10m);

    var kits = (await c.Consulta().Tarefas(Ct)).Valor!.KitsMontaveis;

    Assert.Equal(new[] { 3, 1 }, kits.Select(k => k.Pai.Id).ToArray());
  }

  [Fact]
  public async Task Os_Kits_incompletos_saem_pelo_prazo_do_Pedido_do_pai()
  {
    var c = Cenario();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 40m, 4m, Corte);
    c.NoDoAgrupamento(AgrupamentoId2, 3, null, 10m, null, Solda);
    c.NoDoAgrupamento(AgrupamentoId2, 4, 3, 40m, 4m, Corte);
    c.Kit(AgrupamentoId);
    c.Kit(AgrupamentoId2);
    Pronto(c, 2, Corte, 2m);
    Pronto(c, 4, Corte, 2m);

    var kits = (await c.Consulta().Tarefas(Ct)).Valor!.KitsIncompletos;

    Assert.Equal(new[] { 3, 1 }, kits.Select(k => k.Pai.Id).ToArray());
  }
}

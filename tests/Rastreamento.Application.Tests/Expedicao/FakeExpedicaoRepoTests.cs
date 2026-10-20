using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Expedicao;

/// <summary>
/// O fake e infraestrutura de teste, mas o que ele promete (participar da transacao do fake da execucao, ler as
/// linhas do livro dele e gravar o fechamento como o repositorio real) e contrato para quem o usa.
/// </summary>
public class FakeExpedicaoRepoTests
{
  private static RelatorioDimensional Afericao(int pecaId) => new()
  {
    EstruturaItemId = pecaId, Tipo = TiposDeAfericao.Afericao, Resultado = ResultadosDeAfericao.Conforme,
    UsuarioId = CenarioDeExecucao.Qualidade,
  };

  [Fact]
  public async Task Adicionar_so_vira_registro_no_SalvarAlteracoes_do_Execucao_e_ganha_Id()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null);
    var afericao = Afericao(1);

    c.Expedicao.Adicionar(afericao);
    Assert.Empty(c.Expedicao.Afericoes);

    await c.Execucao.SalvarAlteracoesAsync(CancellationToken.None);

    Assert.Same(afericao, Assert.Single(c.Expedicao.Afericoes));
    Assert.True(afericao.Id > 0);
    Assert.Same(afericao, await c.Expedicao.ObterAfericaoAsync(afericao.Id, CancellationToken.None));
  }

  [Fact]
  public async Task Transacao_desfeita_devolve_os_registros_ao_que_eram()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null);
    var antes = Afericao(1);
    c.Expedicao.Adicionar(antes);
    await c.Execucao.SalvarAlteracoesAsync(CancellationToken.None);

    await c.Execucao.EmTransacaoAsync(async () =>
    {
      c.Expedicao.Adicionar(Afericao(1));
      c.Expedicao.Adicionar(new Remessa { UsuarioId = CenarioDeExecucao.Qualidade });
      await c.Execucao.SalvarAlteracoesAsync(CancellationToken.None);
      return false;
    }, confirmou => confirmou, CancellationToken.None);

    Assert.Same(antes, Assert.Single(c.Expedicao.Afericoes));
    Assert.Empty(c.Expedicao.Remessas);
  }

  [Fact]
  public async Task Pendente_nao_salvo_e_descartado_no_fim_da_transacao()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null);

    await c.Execucao.EmTransacaoAsync(() =>
    {
      c.Expedicao.Adicionar(Afericao(1));
      return Task.FromResult(true);
    }, CancellationToken.None);
    await c.Execucao.SalvarAlteracoesAsync(CancellationToken.None);

    Assert.Empty(c.Expedicao.Afericoes);
  }

  [Fact]
  public async Task Linhas_dos_registros_sao_as_do_livro_do_Execucao_com_o_estorno()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null);
    var original = c.Execucao.Semear(new Movimentacao
    {
      EstruturaItemId = 1, Tipo = TiposDeMovimentacao.Expedicao, Quantidade = 3m, RemessaId = 77,
      OrigemPosicao = Posicoes.NaExpedicao, DestinoPosicao = Posicoes.Expedido,
    });
    var estorno = c.Execucao.Semear(new Movimentacao
    {
      EstruturaItemId = 1, Tipo = TiposDeMovimentacao.Estorno, Quantidade = 3m, RemessaId = 77, EstornoDeId = original.Id,
      OrigemPosicao = Posicoes.Expedido, DestinoPosicao = Posicoes.NaExpedicao,
    });
    c.Execucao.Semear(new Movimentacao
    {
      EstruturaItemId = 1, Tipo = TiposDeMovimentacao.Expedicao, Quantidade = 1m, RemessaId = 78,
      OrigemPosicao = Posicoes.NaExpedicao, DestinoPosicao = Posicoes.Expedido,
    });

    var linhas = await c.Expedicao.ListarLinhasDasRemessasAsync([77], CancellationToken.None);

    Assert.Equal([original.Id, estorno.Id], linhas.Select(m => m.Id));
    Assert.Empty(await c.Expedicao.ListarLinhasDasContagensAsync([77], CancellationToken.None));
  }

  [Fact]
  public async Task Pedidos_de_liberacao_nao_traz_resposta_nem_Pedido_encerrado()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null);
    c.NoDoAgrupamento(CenarioDeExecucao.AgrupamentoId2, 2, null, 4m, null);
    var pedido = new LiberacaoDimensional { EstruturaItemId = 1, Tipo = TiposDeLiberacao.Pedido, Quantidade = 2m, Texto = "x" };
    var resposta = new LiberacaoDimensional
    {
      EstruturaItemId = 1, Tipo = TiposDeLiberacao.Resposta, PedidoDeLiberacaoId = 1, Quantidade = 1m,
    };
    var doEncerrado = new LiberacaoDimensional { EstruturaItemId = 2, Tipo = TiposDeLiberacao.Pedido, Quantidade = 1m, Texto = "y" };
    foreach (var l in new[] { pedido, resposta, doEncerrado }) c.Expedicao.Adicionar(l);
    await c.Execucao.SalvarAlteracoesAsync(CancellationToken.None);
    c.Execucao.StatusDoPedido[CenarioDeExecucao.PedidoId2] = "Concluido";

    var pedidos = await c.Expedicao.ListarPedidosDeLiberacaoAsync(CancellationToken.None);

    Assert.Same(pedido, Assert.Single(pedidos));
  }

  [Fact]
  public async Task Pecas_dos_pedidos_traz_so_as_raizes_dos_Pedidos_pedidos()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null);
    c.No(2, 1, 20m, 2m);
    c.NoDoAgrupamento(CenarioDeExecucao.AgrupamentoId2, 3, null, 4m, null);

    var doPrimeiro = await c.Execucao.ListarPecasDosPedidosAsync([CenarioDeExecucao.PedidoId], CancellationToken.None);
    var dosDois = await c.Execucao.ListarPecasDosPedidosAsync(
        [CenarioDeExecucao.PedidoId, CenarioDeExecucao.PedidoId2], CancellationToken.None);

    Assert.Equal(new PecaDoPedido(CenarioDeExecucao.PedidoId, CenarioDeExecucao.AgrupamentoId, 1, 10m), Assert.Single(doPrimeiro));
    Assert.Equal([1, 3], dosDois.Select(p => p.PecaId));
  }

  [Fact]
  public async Task Gravar_fechamento_preserva_a_data_ja_gravada_e_limpa_quando_nao_concluido()
  {
    var c = new CenarioDeExecucao();
    var t1 = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
    var t2 = t1.AddDays(2);
    FechamentoCalculado Concluido() => new(
        CenarioDeExecucao.PedidoId, "Concluido", true, [new AgrupamentoNoFechamento(CenarioDeExecucao.AgrupamentoId, true)]);

    await c.Execucao.GravarFechamentoAsync(Concluido(), t1, CancellationToken.None);
    await c.Execucao.GravarFechamentoAsync(Concluido(), t2, CancellationToken.None);

    Assert.Equal("Concluido", c.Execucao.StatusDoPedido[CenarioDeExecucao.PedidoId]);
    Assert.Equal(t1, c.Execucao.DataConclusaoDoPedido[CenarioDeExecucao.PedidoId]);
    Assert.Equal(t1, c.Execucao.DataConclusaoDoAgrupamento[CenarioDeExecucao.AgrupamentoId]);

    await c.Execucao.GravarFechamentoAsync(
        new FechamentoCalculado(CenarioDeExecucao.PedidoId, "AguardandoExpedicao", false,
            [new AgrupamentoNoFechamento(CenarioDeExecucao.AgrupamentoId, false)]),
        t2, CancellationToken.None);

    Assert.Equal("AguardandoExpedicao", c.Execucao.StatusDoPedido[CenarioDeExecucao.PedidoId]);
    Assert.Null(c.Execucao.DataConclusaoDoPedido[CenarioDeExecucao.PedidoId]);
    Assert.Null(c.Execucao.DataConclusaoDoAgrupamento[CenarioDeExecucao.AgrupamentoId]);
    // O Agrupamento que a lista nao cita nao ganha entrada.
    Assert.False(c.Execucao.DataConclusaoDoAgrupamento.ContainsKey(CenarioDeExecucao.AgrupamentoId2));
  }
}

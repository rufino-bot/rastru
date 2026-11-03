using Rastreamento.Application.Common;
using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Estrutura;

/// <summary>
/// A estrutura na Fase 5A: a marca `RequerRelatorioDimensional` editavel pelo PCP enquanto a Peca nao tem
/// afericao valida (C14) e o Pedido `Concluido` fechado para escrita de estrutura, com a escrita em Pedido
/// rodando recalculando status e fechamento (C15). Os nos do cenario nao tem Componente, entao a edicao manda
/// a descricao (regra 19).
/// </summary>
public class MontagemDeEstruturaUseCaseTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;
  private static readonly Local NaExpedicao = new(Posicoes.NaExpedicao, null, null);
  private static readonly Local Expedido = new(Posicoes.Expedido, null, null);

  private static CenarioDeExecucao Cenario(bool requerRelatorio = false)
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 4m, null, Corte);
    c.Estruturas.Itens.Single(i => i.Id == 1).RequerRelatorioDimensional = requerRelatorio;
    c.Catalogo.Componentes.Add(new Componente
    {
      Id = 1, Codigo = "C1", Descricao = "Peca do catalogo", Tipo = "Fabricado", Ativo = true, ArquivoSolidoId = 701,
    });
    return c;
  }

  private static void Afericao(CenarioDeExecucao c, int id, string tipo, string? resultado = null, int? estornoDe = null) =>
      c.Expedicao.Afericoes.Add(new RelatorioDimensional
      {
        Id = id, EstruturaItemId = 1, Tipo = tipo, Resultado = resultado, EstornoDeId = estornoDe,
        DataHora = DateTime.UtcNow.AddMinutes(id), UsuarioId = Qualidade,
      });

  private static bool Marca(CenarioDeExecucao c) =>
      c.Estruturas.Itens.Single(i => i.Id == 1).RequerRelatorioDimensional;

  // ---- C14: a marca editavel ----

  [Fact]
  public async Task Editar_liga_RequerRelatorioDimensional_da_Peca()
  {
    var c = Cenario();

    var r = await c.Estrutura().EditarNo(
        1, new EdicaoDeNoDto("No 1", 4m, RequerRelatorioDimensional: true), Ct);

    Assert.True(r.Sucesso);
    Assert.True(Marca(c));
    Assert.True(r.Valor!.RequerRelatorioDimensional);
  }

  [Fact]
  public async Task Editar_desliga_RequerRelatorioDimensional_da_Peca_sem_afericao()
  {
    var c = Cenario(requerRelatorio: true);

    var r = await c.Estrutura().EditarNo(
        1, new EdicaoDeNoDto("No 1", 4m, RequerRelatorioDimensional: false), Ct);

    Assert.True(r.Sucesso);
    Assert.False(Marca(c));
  }

  [Fact]
  public async Task Editar_RequerRelatorioDimensional_em_Item_e_recusado()
  {
    var c = Cenario();
    c.No(2, 1, 8m, 2m, Corte);

    var r = await c.Estrutura().EditarNo(
        2, new EdicaoDeNoDto("No 2", 8m, QuantidadePorPai: 2m, RequerRelatorioDimensional: true), Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaExpedicao.RelatorioSoNaPeca, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.False(c.Estruturas.Itens.Single(i => i.Id == 2).RequerRelatorioDimensional);
  }

  [Fact]
  public async Task Editar_RequerRelatorioDimensional_com_afericao_valida_e_recusado()
  {
    var c = Cenario(requerRelatorio: true);
    Afericao(c, 1, TiposDeAfericao.Afericao, ResultadosDeAfericao.Conforme);

    var r = await c.Estrutura().EditarNo(
        1, new EdicaoDeNoDto("No 1", 4m, RequerRelatorioDimensional: false), Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaExpedicao.ComAfericao, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains("No 1", r.Detalhe);
    Assert.True(Marca(c));
  }

  [Fact]
  public async Task Afericao_estornada_nao_e_valida_e_a_marca_pode_mudar()
  {
    var c = Cenario(requerRelatorio: true);
    Afericao(c, 1, TiposDeAfericao.Afericao, ResultadosDeAfericao.Conforme);
    Afericao(c, 2, TiposDeAfericao.Estorno, estornoDe: 1);

    var r = await c.Estrutura().EditarNo(
        1, new EdicaoDeNoDto("No 1", 4m, RequerRelatorioDimensional: false), Ct);

    Assert.True(r.Sucesso);
    Assert.False(Marca(c));
  }

  [Fact]
  public async Task Repetir_a_marca_atual_com_afericao_valida_nao_e_mudanca_e_edita_o_resto()
  {
    var c = Cenario(requerRelatorio: true);
    Afericao(c, 1, TiposDeAfericao.Afericao, ResultadosDeAfericao.Conforme);

    var r = await c.Estrutura().EditarNo(
        1, new EdicaoDeNoDto("No 1", 4m, RequerRelatorioDimensional: true), Ct);

    Assert.True(r.Sucesso);
    Assert.True(Marca(c));
  }

  [Fact]
  public async Task Editar_com_RequerRelatorioDimensional_nulo_mantem_a_marca()
  {
    var c = Cenario(requerRelatorio: true);
    Afericao(c, 1, TiposDeAfericao.Afericao, ResultadosDeAfericao.Conforme);

    var r = await c.Estrutura().EditarNo(1, new EdicaoDeNoDto("No 1", 4m), Ct);

    Assert.True(r.Sucesso);
    Assert.True(Marca(c));
  }

  // ---- C15: Pedido concluido fechado, Pedido rodando recalcula ----

  [Theory]
  [InlineData("CriarPeca")]
  [InlineData("AcrescentarFilho")]
  [InlineData("EditarNo")]
  public async Task Escrita_de_estrutura_em_Pedido_Concluido_e_recusada(string operacao)
  {
    var c = Cenario();
    c.Execucao.StatusDoPedido[PedidoId] = "Concluido";
    var estrutura = c.Estrutura();

    var r = operacao switch
    {
      "CriarPeca" => await estrutura.CriarPeca(AgrupamentoId, new NovaPecaDto(1, 1m, false), Ct),
      "AcrescentarFilho" => await estrutura.AcrescentarFilho(1, new NovoFilhoDto(null, "Arruela", 2m, 1m), Ct),
      _ => await estrutura.EditarNo(1, new EdicaoDeNoDto("Outro nome", 3m), Ct),
    };

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaExecucao.PedidoFechado, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Single(c.Estruturas.Itens);
    Assert.Equal(4m, c.Estruturas.Itens.Single().Quantidade);
    Assert.Equal("No 1", c.Estruturas.Itens.Single().Descricao);
    Assert.Equal("Concluido", c.Execucao.StatusDoPedido[PedidoId]);
  }

  [Fact]
  public async Task Criar_Peca_em_Pedido_AguardandoExpedicao_volta_a_EmProducao()
  {
    var c = Cenario();
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 4m);
    c.Mover(1, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), NaExpedicao, 4m);
    c.Execucao.StatusDoPedido[PedidoId] = "AguardandoExpedicao";

    var r = await c.Estrutura().CriarPeca(AgrupamentoId, new NovaPecaDto(1, 2m, false), Ct);

    Assert.True(r.Sucesso);
    Assert.Equal("EmProducao", c.Execucao.StatusDoPedido[PedidoId]);
  }

  [Fact]
  public async Task Criar_Peca_em_Pedido_Aberto_nao_mexe_no_status()
  {
    var c = Cenario();

    var r = await c.Estrutura().CriarPeca(AgrupamentoId, new NovaPecaDto(1, 2m, false), Ct);

    Assert.True(r.Sucesso);
    Assert.Equal("Aberto", c.Execucao.StatusDoPedido[PedidoId]);
    Assert.Null(c.Execucao.DataConclusaoDoPedido.GetValueOrDefault(PedidoId));
  }

  [Fact]
  public async Task Baixar_a_quantidade_da_Peca_ate_o_expedido_conclui()
  {
    var c = Cenario();
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 3m);
    c.Mover(1, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), NaExpedicao, 3m);
    c.Mover(1, TiposDeMovimentacao.Expedicao, NaExpedicao, Expedido, 3m);
    c.Execucao.StatusDoPedido[PedidoId] = "EmProducao";

    var r = await c.Estrutura().EditarNo(1, new EdicaoDeNoDto("No 1", 3m), Ct);

    Assert.True(r.Sucesso);
    Assert.Equal("Concluido", c.Execucao.StatusDoPedido[PedidoId]);
    Assert.NotNull(c.Execucao.DataConclusaoDoPedido.GetValueOrDefault(PedidoId));
    Assert.NotNull(c.Execucao.DataConclusaoDoAgrupamento.GetValueOrDefault(AgrupamentoId));
  }

  [Fact]
  public async Task Editar_Peca_trava_o_Pedido_depois_do_no()
  {
    var c = Cenario();

    var r = await c.Estrutura().EditarNo(1, new EdicaoDeNoDto("No 1", 5m), Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(new[] { "nos:1", "pedidos:1" }, c.Execucao.Eventos);
  }
}

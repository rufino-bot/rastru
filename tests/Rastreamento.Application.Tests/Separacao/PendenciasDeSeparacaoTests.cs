using Rastreamento.Application.Separacao;
using Rastreamento.Application.Tests.Common;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Separacao;

/// <summary>
/// A lista do Separador (`GET /separacoes/pendencias`), no cenario de execucao com o planejado compartilhado
/// com o <see cref="FakeSeparacaoRepo"/>. Os dois Pedidos do cenario nascem `Aberto`; a data "de hoje" e
/// 2026-10-16 em Brasilia.
/// </summary>
public class PendenciasDeSeparacaoTests
{
  private const int Parafuso = 7;   // almoxarifado
  private const int Chapa = 8;      // fica no Corte
  private const int Arruela = 9;    // almoxarifado
  private const int Cola = 10;      // almoxarifado

  private static readonly DateOnly Hoje = new(2026, 10, 16);

  private sealed class Cena
  {
    public CenarioDeExecucao C { get; } = new();
    public FakeSeparacaoRepo Separacao { get; }

    public Cena()
    {
      Separacao = new FakeSeparacaoRepo(C.Estruturas);
      Separacao.Materiais[Parafuso] = new MaterialDaSeparacao(Parafuso, "PAR-M8", "Parafuso M8", "UN", true, null, null);
      Separacao.Materiais[Chapa] = new MaterialDaSeparacao(Chapa, "CH-3", "Chapa 3 mm", "KG", true, Corte, "Corte");
      Separacao.Materiais[Arruela] = new MaterialDaSeparacao(Arruela, "ARR-8", "Arruela 8", "UN", true, null, null);
      Separacao.Materiais[Cola] = new MaterialDaSeparacao(Cola, "CO-1", "Cola de contato", "L", true, null, null);
      C.Execucao.DataDeEntregaDoPedido[PedidoId] = new DateOnly(2026, 10, 25);
      C.Execucao.DataDeEntregaDoPedido[PedidoId2] = new DateOnly(2026, 10, 25);
    }

    public ConsultaDeSeparacaoUseCase Uso(DateTimeOffset? agora = null) =>
        new(Separacao, C.Execucao, C.Estruturas, C.Catalogo,
            new RelogioFixo(agora ?? new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)));

    /// <summary>Planeja `quantidade` do material no no (o planejado e o da arvore: compartilhado com o fake).</summary>
    public void Planejar(int noId, int materialId, decimal quantidade)
    {
      var material = Separacao.Materiais[materialId];
      C.Material(noId, materialId, material.Codigo, material.Descricao);
      C.Estruturas.Materiais[^1].Quantidade = quantidade;
    }

    public async Task SepararAsync(int noId, int materialId, decimal quantidade) =>
        await Separacao.GravarAsync(
        [
          new MaterialSeparacao
          {
            EstruturaItemId = noId, MaterialId = materialId, Tipo = TiposDeSeparacao.Separacao, Quantidade = quantidade,
            DataHora = DateTime.UtcNow, UsuarioId = Operador,
          },
        ], CancellationToken.None);

    public async Task<IReadOnlyList<PendenciaDeSeparacaoDto>> ListarAsync(DateTimeOffset? agora = null)
    {
      var r = await Uso(agora).Pendencias(CancellationToken.None);
      Assert.True(r.Sucesso, r.Detalhe);
      return r.Valor!;
    }
  }

  [Fact]
  public async Task Lista_so_no_com_pendencia_em_material_do_almoxarifado()
  {
    var c = new Cena();
    c.C.No(1, null, 5m, null, Corte);   // A: parafuso pendente
    c.C.No(2, null, 5m, null, Corte);   // B: so chapa, que fica num Setor
    c.C.No(3, null, 5m, null, Corte);   // C: parafuso completo
    c.C.No(4, null, 5m, null, Corte);   // D: sem material nenhum
    c.Planejar(1, Parafuso, 10m);
    c.Planejar(2, Chapa, 4m);
    c.Planejar(3, Parafuso, 10m);
    await c.SepararAsync(3, Parafuso, 10m);

    var lista = await c.ListarAsync();

    var a = Assert.Single(lista);
    Assert.Equal(1, a.No.Id);
    Assert.Equal("No 1", a.No.Descricao);
    Assert.Null(a.No.CodigoDoComponente);
    Assert.Equal((AgrupamentoId, "AG-01"), (a.No.AgrupamentoId, a.No.AgrupamentoCodigo));
    Assert.Equal((PedidoId, "PED-01", "Cliente do cenário"), (a.Pedido.Id, a.Pedido.Numero, a.Pedido.Cliente));
    Assert.Equal(new DateOnly(2026, 10, 25), a.Pedido.DataEntrega);
  }

  [Fact]
  public async Task Ordena_pela_data_de_entrega_depois_pedido_depois_no()
  {
    var c = new Cena();
    c.C.No(5, null, 5m, null, Corte);
    c.C.No(3, null, 5m, null, Corte);
    c.C.NoDoAgrupamento(AgrupamentoId2, 4, null, 5m, null, Corte);
    foreach (var no in new[] { 5, 3, 4 }) c.Planejar(no, Parafuso, 10m);
    c.C.Execucao.DataDeEntregaDoPedido[PedidoId] = new DateOnly(2026, 10, 25);
    c.C.Execucao.DataDeEntregaDoPedido[PedidoId2] = new DateOnly(2026, 10, 20);

    // O prazo do Pedido 2 e o mais proximo: o no 4 vem primeiro, embora o Pedido 1 tenha o menor Id.
    Assert.Equal([4, 3, 5], (await c.ListarAsync()).Select(p => p.No.Id));

    // Mesmo prazo: o Pedido de menor Id primeiro e, dentro dele, o no de menor Id.
    c.C.Execucao.DataDeEntregaDoPedido[PedidoId2] = new DateOnly(2026, 10, 25);
    Assert.Equal([3, 5, 4], (await c.ListarAsync()).Select(p => p.No.Id));
  }

  [Fact]
  public async Task Destino_e_o_primeiro_passo_do_Roteiro_e_nulo_sem_Roteiro()
  {
    var c = new Cena();
    c.C.No(1, null, 5m, null, Dobra, Solda);   // primeiro passo: Dobra
    c.C.No(2, null, 5m, null);                  // sem Roteiro
    c.Planejar(1, Parafuso, 10m);
    c.Planejar(2, Parafuso, 10m);

    var lista = await c.ListarAsync();

    Assert.Equal(new SetorDeDestinoDto(Dobra, "Dobra"), lista.Single(p => p.No.Id == 1).Destino);
    Assert.Null(lista.Single(p => p.No.Id == 2).Destino);
  }

  [Fact]
  public async Task Pedido_pausado_continua_na_lista_com_a_pausa()
  {
    var c = new Cena();
    c.C.No(1, null, 5m, null, Corte);
    c.C.NoDoAgrupamento(AgrupamentoId2, 2, null, 5m, null, Corte);
    c.Planejar(1, Parafuso, 10m);
    c.Planejar(2, Parafuso, 10m);
    var desde = new DateTime(2026, 10, 15, 14, 0, 0, DateTimeKind.Utc);
    c.C.Execucao.Pausas.Add(new PedidoPausa
    {
      Id = 1, PedidoId = PedidoId, PausadoEm = desde, PausadoPorUsuarioId = Pcp, Motivo = "Falta chapa",
    });

    var lista = await c.ListarAsync();

    Assert.Equal([1, 2], lista.Select(p => p.No.Id));
    var pausa = lista[0].Pedido.Pausa;
    Assert.NotNull(pausa);
    Assert.Equal((desde, "PCP", "Falta chapa"), (pausa.Desde, pausa.PorUsuarioNome, pausa.Motivo));
    Assert.Null(lista[1].Pedido.Pausa);
  }

  [Fact]
  public async Task Pedido_atrasado_vem_marcado()
  {
    var c = new Cena();
    c.C.No(1, null, 5m, null, Corte);
    c.C.NoDoAgrupamento(AgrupamentoId2, 2, null, 5m, null, Corte);
    c.Planejar(1, Parafuso, 10m);
    c.Planejar(2, Parafuso, 10m);
    c.C.Execucao.DataDeEntregaDoPedido[PedidoId] = Hoje.AddDays(-1);   // ontem em Brasilia
    c.C.Execucao.DataDeEntregaDoPedido[PedidoId2] = Hoje;              // vence hoje: ainda nao e atraso

    var lista = await c.ListarAsync();

    Assert.True(lista.Single(p => p.No.Id == 1).Pedido.Atrasado);
    Assert.False(lista.Single(p => p.No.Id == 2).Pedido.Atrasado);

    // 22h em Brasilia ainda e dia 16; em UTC ja e dia 17, e o atraso nao pode nascer da data UTC.
    var noite = await c.ListarAsync(new DateTimeOffset(2026, 10, 17, 1, 0, 0, TimeSpan.Zero));
    Assert.False(noite.Single(p => p.No.Id == 2).Pedido.Atrasado);
    var amanha = await c.ListarAsync(new DateTimeOffset(2026, 10, 17, 4, 0, 0, TimeSpan.Zero));
    Assert.True(amanha.Single(p => p.No.Id == 2).Pedido.Atrasado);
  }

  [Theory]
  [InlineData("Concluido")]
  [InlineData("Cancelado")]
  public async Task Pedido_concluido_ou_cancelado_nao_entra(string status)
  {
    var c = new Cena();
    c.C.No(1, null, 5m, null, Corte);
    c.C.NoDoAgrupamento(AgrupamentoId2, 2, null, 5m, null, Corte);
    c.Planejar(1, Parafuso, 10m);
    c.Planejar(2, Parafuso, 10m);
    c.C.Execucao.StatusDoPedido[PedidoId] = status;

    var lista = await c.ListarAsync();

    Assert.Equal(2, Assert.Single(lista).No.Id);
  }

  [Fact]
  public async Task Materiais_da_pendencia_sao_so_os_pendentes_do_almoxarifado()
  {
    var c = new Cena();
    c.C.No(1, null, 5m, null, Corte);
    c.Planejar(1, Parafuso, 10m);   // pendente: separado 4 de 10
    c.Planejar(1, Arruela, 8m);     // pendente, e o codigo vem antes: ARR-8 < PAR-M8
    c.Planejar(1, Chapa, 3m);       // fica num Setor: nunca pendente
    c.Planejar(1, Cola, 2m);        // do almoxarifado e completo
    await c.SepararAsync(1, Parafuso, 4m);
    await c.SepararAsync(1, Cola, 2m);

    var lista = await c.ListarAsync();

    var materiais = Assert.Single(lista).Materiais;
    Assert.Equal(["ARR-8", "PAR-M8"], materiais.Select(m => m.Codigo));
    var parafuso = materiais.Single(m => m.MaterialId == Parafuso);
    Assert.Equal(("Parafuso M8", "UN"), (parafuso.Descricao, parafuso.UnidadeMedida));
    Assert.Equal((10m, 4m, 6m), (parafuso.Planejado, parafuso.Separado, parafuso.Pendencia));
    var arruela = materiais.Single(m => m.MaterialId == Arruela);
    Assert.Equal((8m, 0m, 8m), (arruela.Planejado, arruela.Separado, arruela.Pendencia));
  }

  [Fact]
  public async Task Sem_no_pendente_a_lista_e_vazia()
  {
    var c = new Cena();
    c.C.No(1, null, 5m, null, Corte);

    Assert.Empty(await c.ListarAsync());
  }
}

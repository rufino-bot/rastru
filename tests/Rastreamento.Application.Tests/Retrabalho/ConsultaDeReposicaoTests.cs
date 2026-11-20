using Rastreamento.Application.Retrabalho;
using Rastreamento.Application.Tests.Baixa;
using Rastreamento.Application.Tests.Estrutura;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Retrabalho;

/// <summary>
/// A conta do "a repor" (secao 4.1 e C1 da spec da Fase 5B-2), sem banco: unidades de Peca que saem por baixa de perda
/// ou voltam por reclamacao, enquanto vigentes e sem Retrabalho nem dispensa.
/// </summary>
public class ConsultaDeReposicaoTests
{
  private const int Pedido = 7;

  private static BaixaParaContar Baixa(
      string motivo, decimal unidadesDePeca, bool estornada = false, bool reposta = false, bool dispensada = false,
      int pedido = Pedido) =>
      new(pedido, motivo, unidadesDePeca, estornada, reposta, dispensada);

  private static ReclamacaoParaContar Reclamacao(
      decimal quantidade, bool estornada = false, bool reposta = false, bool dispensada = false, int pedido = Pedido) =>
      new(pedido, quantidade, estornada, reposta, dispensada);

  [Fact]
  public void A_repor_soma_baixas_e_reclamacoes_em_unidades_de_Peca()
  {
    // Uma baixa de Item que levou 2 Pecas pela caixa do topo + uma reclamacao de 1.
    var contagem = ConsultaDeReposicao.ContarPorPedido(
        [Baixa(MotivosDePerda.PerdaArmazem, 2m)], [Reclamacao(1m)]);

    Assert.Equal(3m, contagem[Pedido]);
  }

  [Fact]
  public void A_perda_de_Item_que_nao_levou_Peca_nao_acende_nada()
  {
    Assert.Equal(0m, ConsultaDeReposicao.UnidadesARepor(Baixa(MotivosDePerda.MortaEmProcesso, 0m)));
  }

  [Theory]
  [InlineData(MotivosDePerda.Descarte)]
  [InlineData(MotivosDePerda.Reaproveitado)]
  public void Descarte_e_Reaproveitado_nunca_acendem(string motivo)
  {
    Assert.Equal(0m, ConsultaDeReposicao.UnidadesARepor(Baixa(motivo, 5m)));
  }

  [Theory]
  [InlineData(true, false, false)]
  [InlineData(false, true, false)]
  [InlineData(false, false, true)]
  public void Baixa_estornada_reposta_ou_dispensada_nao_conta(bool estornada, bool reposta, bool dispensada)
  {
    Assert.Equal(0m, ConsultaDeReposicao.UnidadesARepor(
        Baixa(MotivosDePerda.PerdaArmazem, 4m, estornada, reposta, dispensada)));
    Assert.Equal(0m, ConsultaDeReposicao.UnidadesARepor(Reclamacao(4m, estornada, reposta, dispensada)));
  }

  [Fact]
  public void Cada_Pedido_recebe_a_sua_soma_e_o_que_nao_tem_nada_fica_de_fora()
  {
    var contagem = ConsultaDeReposicao.ContarPorPedido(
        [Baixa(MotivosDePerda.PerdaArmazem, 2m, pedido: 1), Baixa(MotivosDePerda.MortaEmProcesso, 3m, pedido: 2)],
        [Reclamacao(1m, pedido: 2), Reclamacao(5m, estornada: true, pedido: 3)]);

    Assert.Equal(2m, contagem[1]);
    Assert.Equal(4m, contagem[2]);
    Assert.Equal(0m, contagem.GetValueOrDefault(3));
  }

  /// <summary>Duas Pecas de um Pedido (cada uma com um Item) e uma de outro, sobre os fakes; as baixas e reclamacoes o teste poe.</summary>
  private sealed class Cenario
  {
    public readonly FakeEstruturaRepo Estruturas = new();
    public readonly FakeExecucaoRepo Execucao;
    public readonly FakeReposicaoRepo Reposicao = new();
    public readonly ConsultaDeReposicao Consulta;
    private int _perda = 100;
    private int _linha = 1000;

    public Cenario()
    {
      Execucao = new FakeExecucaoRepo(Estruturas);
      Consulta = new ConsultaDeReposicao(Reposicao, new FakeBaixaRepo(Execucao, Estruturas), Execucao);
      Estruturas.Itens.Add(new EstruturaItem { Id = 1, AgrupamentoId = 10, NivelHierarquico = "Peca", Quantidade = 5m });
      Estruturas.Itens.Add(new EstruturaItem { Id = 2, AgrupamentoId = 10, NivelHierarquico = "Item", EstruturaPaiId = 1, Quantidade = 10m });
      Estruturas.Itens.Add(new EstruturaItem { Id = 3, AgrupamentoId = 10, NivelHierarquico = "Peca", Quantidade = 4m });
      Estruturas.Itens.Add(new EstruturaItem { Id = 4, AgrupamentoId = 20, NivelHierarquico = "Peca", Quantidade = 9m });
    }

    /// <summary>Uma baixa do Pedido com uma linha `Perda` por (no, quantidade); devolve o Id dela.</summary>
    public int Baixa(int pedidoId, string motivo, params (int No, decimal Quantidade)[] linhas)
    {
      var id = ++_perda;
      Reposicao.PerdasSemRetrabalho.Add(new Perda { Id = id, PedidoId = pedidoId, Motivo = motivo });
      foreach (var (no, quantidade) in linhas)
        Execucao.Movimentacoes.Add(new Movimentacao
        {
          Id = ++_linha, PerdaId = id, EstruturaItemId = no, Tipo = TiposDeMovimentacao.Perda, Quantidade = quantidade,
        });
      return id;
    }

    public void Estornar(int perdaId) =>
        Execucao.Movimentacoes.Add(new Movimentacao { Id = ++_linha, PerdaId = perdaId, EstruturaItemId = 1, Tipo = TiposDeMovimentacao.Estorno });
  }

  [Fact]
  public async Task A_repor_por_peca_soma_o_a_repor_do_pedido()
  {
    var c = new Cenario();
    // Pedido 1: uma baixa que leva 2 da Peca 1 (e 6 do Item 2, que nao e Peca), uma de 1 da Peca 3 e uma reclamacao de 3 na Peca 1.
    c.Baixa(1, MotivosDePerda.PerdaArmazem, (1, 2m), (2, 6m));
    c.Baixa(1, MotivosDePerda.MortaEmProcesso, (3, 1m));
    c.Reposicao.ReclamacoesSemRetrabalho.Add(new Reclamacao { Id = 1, PedidoId = 1, EstruturaItemId = 1, Quantidade = 3m });
    // Pedido 2 e uma baixa estornada do Pedido 1 nao contam.
    c.Baixa(2, MotivosDePerda.PerdaArmazem, (4, 7m));
    var estornada = c.Baixa(1, MotivosDePerda.PerdaArmazem, (3, 8m));
    c.Estornar(estornada);

    var porPeca = await c.Consulta.AReporPorPeca([1, 2], CancellationToken.None);
    var porPedido = await c.Consulta.ARepor([1, 2], CancellationToken.None);

    Assert.Equal(5m, porPeca[1]);
    Assert.Equal(1m, porPeca[3]);
    Assert.Equal(7m, porPeca[4]);
    Assert.False(porPeca.ContainsKey(2));   // o Item nao e Peca
    Assert.Equal(porPedido[1], porPeca[1] + porPeca[3]);
    Assert.Equal(porPedido[2], porPeca[4]);
  }

  [Fact]
  public async Task Descartar_tudo_marca_cada_peca_pela_linha_dela()
  {
    var c = new Cenario();
    // Uma baixa de Agrupamento inteiro: as Pecas 1 e 3 saem na mesma baixa, cada uma com a sua quantidade.
    c.Baixa(1, MotivosDePerda.PerdaArmazem, (1, 5m), (2, 10m), (3, 4m));
    // O descarte nunca acende nada, ainda que leve Peca.
    c.Baixa(1, MotivosDePerda.Descarte, (1, 9m));

    var porPeca = await c.Consulta.AReporPorPeca([1], CancellationToken.None);

    Assert.Equal(5m, porPeca[1]);
    Assert.Equal(4m, porPeca[3]);
    Assert.Equal(2, porPeca.Count);
    Assert.Empty(await c.Consulta.AReporPorPeca([], CancellationToken.None));
  }
}

using Rastreamento.Application.Retrabalho;
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
}

using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Tests.Common;
using Xunit;

namespace Rastreamento.Application.Tests.Cadastros;

public class PrazoDeEntregaTests
{
  private static readonly DateOnly Hoje = new(2026, 10, 8);

  [Theory]
  [InlineData("Aberto")]
  [InlineData("EmProducao")]
  [InlineData("AguardandoExpedicao")]
  public void Prazo_de_ontem_num_Pedido_nao_encerrado_esta_atrasado(string status) =>
      Assert.True(PrazoDeEntrega.EstaAtrasado(Hoje.AddDays(-1), status, Hoje));

  [Fact]
  public void Prazo_de_hoje_ainda_nao_esta_atrasado() =>
      Assert.False(PrazoDeEntrega.EstaAtrasado(Hoje, "Aberto", Hoje));

  [Fact]
  public void Prazo_futuro_nao_esta_atrasado() =>
      Assert.False(PrazoDeEntrega.EstaAtrasado(Hoje.AddDays(1), "Aberto", Hoje));

  [Theory]
  [InlineData("Concluido")]
  [InlineData("Cancelado")]
  public void Pedido_encerrado_nunca_esta_atrasado(string status) =>
      Assert.False(PrazoDeEntrega.EstaAtrasado(Hoje.AddDays(-30), status, Hoje));

  [Fact]
  public void Os_encerrados_sao_Concluido_e_Cancelado() =>
      Assert.Equal(["Cancelado", "Concluido"], PrazoDeEntrega.StatusEncerrados.Order());

  // A virada do dia em Brasilia (GMT-3 fixo) e as 03h00 UTC. As duas pontas fixam o offset em exatamente
  // -3h: a das 02h59 recusa todo offset acima de -3h (o UTC e o +3h inclusive), e a das 03h00 recusa todo
  // offset abaixo de -3h.
  [Fact]
  public void As_02h59_UTC_ainda_e_o_dia_anterior_em_Brasilia() =>
      Assert.Equal(
          new DateOnly(2026, 10, 7),
          PrazoDeEntrega.HojeEmBrasilia(new RelogioFixo(new DateTimeOffset(2026, 10, 8, 2, 59, 59, TimeSpan.Zero))));

  [Fact]
  public void As_03h00_UTC_ja_e_o_dia_seguinte_em_Brasilia() =>
      Assert.Equal(
          new DateOnly(2026, 10, 8),
          PrazoDeEntrega.HojeEmBrasilia(new RelogioFixo(new DateTimeOffset(2026, 10, 8, 3, 0, 0, TimeSpan.Zero))));
}

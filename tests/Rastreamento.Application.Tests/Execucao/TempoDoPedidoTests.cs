using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Execucao;

public class TempoDoPedidoTests
{
  private static readonly DateTime H0 = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
  private static DateTime H(int horas) => H0.AddHours(horas);

  private static Movimentacao Linha(int id, string tipo, DateTime quando, int? estornoDe = null) => new()
  {
    Id = id, EstruturaItemId = 1, Tipo = tipo, Quantidade = 1m, DataHora = quando, EstornoDeId = estornoDe,
  };

  private static PedidoPausa Pausa(DateTime de, DateTime? ate) => new() { PausadoEm = de, RetomadoEm = ate };

  [Fact]
  public void Sem_inicio_e_nulo() =>
      Assert.Null(TempoDoPedido.InicioReal([]));

  [Fact]
  public void Menor_inicio_entre_os_nos() =>
      Assert.Equal(H(2), TempoDoPedido.InicioReal([
        Linha(1, TiposDeMovimentacao.Inicio, H(5)), Linha(2, TiposDeMovimentacao.Inicio, H(2)),
        Linha(3, TiposDeMovimentacao.Inicio, H(9))]));

  [Fact]
  public void Inicio_estornado_nao_conta() =>
      Assert.Equal(H(5), TempoDoPedido.InicioReal([
        Linha(1, TiposDeMovimentacao.Inicio, H(2)), Linha(2, TiposDeMovimentacao.Inicio, H(5)),
        Linha(3, TiposDeMovimentacao.Estorno, H(3), estornoDe: 1)]));

  [Fact]
  public void Devolucao_conta_como_inicio() =>
      Assert.Equal(H(1), TempoDoPedido.InicioReal([
        Linha(1, TiposDeMovimentacao.Inicio, H(4)), Linha(2, TiposDeMovimentacao.Devolucao, H(1))]));

  [Fact]
  public void Pronto_nao_conta() =>
      Assert.Equal(H(4), TempoDoPedido.InicioReal([
        Linha(1, TiposDeMovimentacao.Pronto, H(1)), Linha(2, TiposDeMovimentacao.Inicio, H(4))]));

  [Fact]
  public void Pausa_antes_do_inicio_nao_conta() =>
      Assert.Equal(TimeSpan.Zero, TempoDoPedido.PausadoDepoisDe(H(10), [Pausa(H(1), H(5))], H(20)));

  [Fact]
  public void Pausa_que_cruza_o_inicio_conta_so_a_parte_depois() =>
      Assert.Equal(TimeSpan.FromHours(2), TempoDoPedido.PausadoDepoisDe(H(10), [Pausa(H(5), H(12))], H(20)));

  [Fact]
  public void Pausa_aberta_conta_ate_agora() =>
      Assert.Equal(TimeSpan.FromHours(3), TempoDoPedido.PausadoDepoisDe(H(2), [Pausa(H(7), null)], H(10)));
}

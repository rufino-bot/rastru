using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Execucao;

public class LivroTests
{
  private static Movimentacao Mov(int item, string tipo, Local de, Local para, decimal quantidade) => new()
  {
    EstruturaItemId = item, Tipo = tipo, Quantidade = quantidade,
    OrigemPosicao = de.Posicao, OrigemSetorId = de.SetorId, OrigemOrdem = de.Ordem,
    DestinoPosicao = para.Posicao, DestinoSetorId = para.SetorId, DestinoOrdem = para.Ordem,
  };

  [Fact]
  public void Soma_destino_e_subtrai_origem_por_no_e_posicao()
  {
    var saldos = Livro.SomarSaldos(
    [
      Mov(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(7, 1), 6m),
      Mov(1, TiposDeMovimentacao.Termino, Local.NoSetor(7, 1), Local.AguardandoColeta(7, 1), 4m),
      Mov(2, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(7, 1), 2m),
    ]);

    Assert.Equal(
        new[]
        {
          new SaldoLiquido(1, Posicoes.AIniciar, null, null, -6m),
          new SaldoLiquido(1, Posicoes.NoSetor, 7, 1, 2m),
          new SaldoLiquido(1, Posicoes.AguardandoColeta, 7, 1, 4m),
          new SaldoLiquido(2, Posicoes.AIniciar, null, null, -2m),
          new SaldoLiquido(2, Posicoes.NoSetor, 7, 1, 2m),
        },
        saldos);
  }

  [Fact]
  public void Estorno_zera_o_par_e_posicao_zerada_nao_aparece()
  {
    var saldos = Livro.SomarSaldos(
    [
      Mov(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(7, 1), 6m),
      Mov(1, TiposDeMovimentacao.Estorno, Local.NoSetor(7, 1), Local.AIniciar, 6m),
    ]);

    Assert.Empty(saldos);
  }
}

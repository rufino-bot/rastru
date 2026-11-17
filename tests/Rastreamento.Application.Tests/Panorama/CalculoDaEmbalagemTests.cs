using Rastreamento.Application.Execucao;
using Rastreamento.Application.Panorama;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Panorama;

/// <summary>O "Embalado" calculado na leitura (regra 40, C18 e o "some sozinho" da spec do Panorama).</summary>
public class CalculoDaEmbalagemTests
{
  private const int Corte = 1;
  private static readonly DateTime Agora = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);

  private static IReadOnlyList<(Local, decimal)> Saldos(params (Local, decimal)[] s) => s;

  private static UltimaEmbalagem Marca(bool voltou = false) => new(7, TiposDeEmbalagem.Marcar, Agora, 1, voltou);

  [Fact]
  public void Peca_toda_no_local_de_expedicao_pode_marcar()
  {
    var e = CalculoDaEmbalagem.Estado(Saldos((Local.Conforme, 5m)), null);

    Assert.True(e.PodeMarcar);
    Assert.Equal(0m, e.EmProducao);
  }

  [Fact]
  public void Parte_expedida_e_o_resto_no_local_pode_marcar()
  {
    var e = CalculoDaEmbalagem.Estado(Saldos((Local.Expedido, 2m), (Local.NaExpedicao, 3m)), null);

    Assert.True(e.PodeMarcar);
  }

  [Fact]
  public void Toda_expedida_nao_pode_marcar()
  {
    var e = CalculoDaEmbalagem.Estado(Saldos((Local.Expedido, 5m)), null);

    Assert.False(e.PodeMarcar);
  }

  [Theory]
  [InlineData(Posicoes.AIniciar)]
  [InlineData(Posicoes.NoSetor)]
  [InlineData(Posicoes.AguardandoColeta)]
  [InlineData(Posicoes.AguardandoMontagem)]
  [InlineData(Posicoes.AguardandoConserto)]
  [InlineData(Posicoes.EmConserto)]
  [InlineData(Posicoes.ConsertoPronto)]
  public void Uma_em_producao_nao_pode_marcar(string posicao)
  {
    var local = posicao switch
    {
      Posicoes.AIniciar => Local.AIniciar,
      Posicoes.NoSetor => Local.NoSetor(Corte, 1),
      Posicoes.AguardandoColeta => Local.AguardandoColeta(Corte, 1),
      Posicoes.AguardandoMontagem => Local.AguardandoMontagem(Corte),
      Posicoes.AguardandoConserto => Local.AguardandoConserto(Corte),
      Posicoes.EmConserto => Local.EmConserto(Corte),
      _ => Local.ConsertoPronto(Corte),
    };

    var e = CalculoDaEmbalagem.Estado(Saldos((local, 1m), (Local.Conforme, 4m)), null);

    Assert.False(e.PodeMarcar);
    Assert.Equal(1m, e.EmProducao);
  }

  [Fact]
  public void Nao_conforme_e_aguardando_liberacao_contam_como_local()
  {
    var e = CalculoDaEmbalagem.Estado(Saldos((Local.NaoConforme, 2m), (Local.AguardandoLiberacao, 1m)), null);

    Assert.True(e.PodeMarcar);
    Assert.Equal(0m, e.EmProducao);
  }

  [Fact]
  public void Sem_registro_nao_vale()
  {
    Assert.False(CalculoDaEmbalagem.Estado(Saldos((Local.Conforme, 5m)), null).Vale);
  }

  [Fact]
  public void Ultima_desmarcar_nao_vale()
  {
    var ultima = new UltimaEmbalagem(7, TiposDeEmbalagem.Desmarcar, Agora, 1, false);

    Assert.False(CalculoDaEmbalagem.Estado(Saldos((Local.Conforme, 5m)), ultima).Vale);
  }

  [Fact]
  public void Marcada_e_podendo_marcar_vale()
  {
    Assert.True(CalculoDaEmbalagem.Estado(Saldos((Local.Conforme, 5m)), Marca()).Vale);
  }

  [Fact]
  public void Movimento_para_producao_depois_da_marca_invalida()
  {
    Assert.False(CalculoDaEmbalagem.Estado(Saldos((Local.Conforme, 5m)), Marca(voltou: true)).Vale);
  }

  [Fact]
  public void Marcada_com_producao_agora_nao_vale()
  {
    // A Peca com a quantidade aumentada depois da marca: 1 unidade em "a iniciar".
    var e = CalculoDaEmbalagem.Estado(Saldos((Local.Conforme, 5m), (Local.AIniciar, 1m)), Marca());

    Assert.False(e.Vale);
    Assert.False(e.PodeMarcar);
  }
}

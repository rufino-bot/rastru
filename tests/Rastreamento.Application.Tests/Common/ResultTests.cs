using Rastreamento.Application.Common;
using Xunit;

namespace Rastreamento.Application.Tests.Common;

public class ResultTests
{
  [Fact]
  public void Ok_com_valor_nao_carrega_erro_nem_tipo()
  {
    var r = Result<int>.Ok(42);

    Assert.True(r.Sucesso);
    Assert.Equal(42, r.Valor);
    Assert.Null(r.Erro);
    Assert.Null(r.TipoDoErro);
  }

  [Fact]
  public void Falha_sem_tipo_explicito_e_de_validacao()
  {
    var r = Result<int>.Falha("invalido");

    Assert.False(r.Sucesso);
    Assert.Equal(default, r.Valor);
    Assert.Equal("invalido", r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
  }

  [Theory]
  [InlineData(TipoDeErro.Validacao)]
  [InlineData(TipoDeErro.NaoEncontrado)]
  [InlineData(TipoDeErro.Conflito)]
  [InlineData(TipoDeErro.NaoAutorizado)]
  public void Falha_preserva_o_tipo_informado(TipoDeErro tipo)
  {
    Assert.Equal(tipo, Result<int>.Falha("x", tipo).TipoDoErro);
    Assert.Equal(tipo, Result.Falha("x", tipo).TipoDoErro);
  }

  [Fact]
  public void Result_sem_valor_distingue_sucesso_de_falha()
  {
    var ok = Result.Ok();
    var falha = Result.Falha("nao encontrado", TipoDeErro.NaoEncontrado);

    Assert.True(ok.Sucesso);
    Assert.Null(ok.Erro);
    Assert.Null(ok.TipoDoErro);

    Assert.False(falha.Sucesso);
    Assert.Equal("nao encontrado", falha.Erro);
    Assert.Equal(TipoDeErro.NaoEncontrado, falha.TipoDoErro);
  }
}

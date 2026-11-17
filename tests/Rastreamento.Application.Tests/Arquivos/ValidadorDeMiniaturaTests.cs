using Rastreamento.Application.Arquivos;

namespace Rastreamento.Application.Tests.Arquivos;

public class ValidadorDeMiniaturaTests
{
  [Fact]
  public void Png_320_por_240_passa() =>
      Assert.Null(ValidadorDeMiniatura.Validar(PngDeTeste.Png(320, 240)));

  [Fact]
  public void Vazio_recusa() =>
      Assert.Equal("A miniatura esta vazia.", ValidadorDeMiniatura.Validar([]));

  [Fact]
  public void Sem_assinatura_png_recusa() =>
      Assert.Equal("A miniatura nao e um PNG.", ValidadorDeMiniatura.Validar(StlDeTeste.CuboBinario()));

  [Theory]
  [InlineData(321, 240)]
  [InlineData(320, 241)]
  [InlineData(240, 320)]
  public void Outra_dimensao_recusa(int l, int a) =>
      Assert.Equal("A miniatura precisa ter 320 x 240 pixels.",
          ValidadorDeMiniatura.Validar(PngDeTeste.Png(l, a)));

  [Fact]
  public void Acima_de_512_KiB_recusa() =>
      Assert.Equal("A miniatura passa de 512 KiB.",
          ValidadorDeMiniatura.Validar(PngDeTeste.Png(320, 240, 512 * 1024)));

  [Fact]
  public void Png_truncado_antes_do_IHDR_recusa() =>
      Assert.Equal("A miniatura nao e um PNG.",
          ValidadorDeMiniatura.Validar(PngDeTeste.Png(320, 240)[..12]));
}

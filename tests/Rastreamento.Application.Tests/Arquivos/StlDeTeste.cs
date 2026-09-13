namespace Rastreamento.Application.Tests.Arquivos;

/// <summary>
/// STL binario valido, gerado em codigo. Um cubo tem 6 faces, 2 triangulos cada = 12 triangulos,
/// logo 84 + 50*12 = 684 bytes EXATOS. A geometria e irrelevante para a validacao (nenhuma camada
/// olha as coordenadas); o que importa e a FORMA do arquivo.
/// </summary>
public static class StlDeTeste
{
  public const int Triangulos = 12;

  public const int TamanhoEsperado = 84 + 50 * Triangulos;   // 684

  public static byte[] CuboBinario() => Binario(Triangulos);

  /// <summary>STL binario bem-formado com a contagem pedida. `n` = 0 produz um arquivo de 84
  /// bytes, que e estruturalmente coerente e ainda assim nao e solido nenhum.</summary>
  public static byte[] Binario(int triangulos)
  {
    var bytes = new byte[84 + 50 * triangulos];
    // Os 80 bytes de cabecalho ficam zerados de proposito: um STL binario de verdade costuma
    // trazer texto ali, e ha arquivos reais cujo cabecalho comeca com "solid" — e por isso que o
    // validador tenta a forma BINARIA primeiro.
    BitConverter.GetBytes(triangulos).CopyTo(bytes, 80);
    return bytes;
  }

  /// <summary>Binario cujo cabecalho comeca com "solid": a armadilha classica de sniffing.</summary>
  public static byte[] BinarioComCabecalhoQueDizSolid()
  {
    var bytes = Binario(Triangulos);
    "solid cubo exportado"u8.ToArray().CopyTo(bytes, 0);
    return bytes;
  }

  public static byte[] Ascii() => System.Text.Encoding.UTF8.GetBytes(
      """
      solid cubo
        facet normal 0 0 1
          outer loop
            vertex 0 0 0
            vertex 1 0 0
            vertex 1 1 0
          endloop
        endfacet
      endsolid cubo
      """);
}

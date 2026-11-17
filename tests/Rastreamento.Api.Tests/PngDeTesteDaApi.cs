using System.Buffers.Binary;

namespace Rastreamento.Api.Tests;

/// <summary>
/// PNG minimo gerado em codigo, para os testes de ponta a ponta da miniatura do solido: a
/// assinatura e o chunk IHDR com largura e altura em big-endian. Nao e uma imagem decodificavel --
/// o <c>ValidadorDeMiniatura</c> so le a assinatura e o IHDR.
///
/// <para>
/// DUPLICADO de proposito em relacao a <c>PngDeTeste</c>
/// (<c>Rastreamento.Application.Tests.Arquivos</c>), pelo mesmo motivo de
/// <see cref="StlDeTesteDaApi"/>: os projetos de teste nao se referenciam.
/// </para>
/// </summary>
public static class PngDeTesteDaApi
{
  /// <summary><paramref name="marcador"/> vai no fim do arquivo: dois PNGs validos da mesma
  /// dimensao com bytes (e portanto versoes) diferentes.</summary>
  public static byte[] Png(int largura, int altura, byte marcador = 0)
  {
    var bytes = new byte[8 + 4 + 4 + 13 + 4 + 1];
    new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
    BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8, 4), 13);
    "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
    BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), largura);
    BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), altura);
    bytes[^1] = marcador;
    return bytes;
  }
}

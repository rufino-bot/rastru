namespace Rastreamento.Application.Tests.Arquivos;

/// <summary>
/// PNG minimo gerado em codigo: assinatura + chunk IHDR com largura e altura em big-endian. Nao e
/// uma imagem decodificavel e o CRC fica zerado: o validador so le a assinatura e o IHDR.
/// </summary>
public static class PngDeTeste
{
  public static byte[] Png(int largura, int altura, int bytesExtras = 0)
  {
    var bytes = new byte[8 + 4 + 4 + 13 + 4 + bytesExtras];
    new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
    // Comprimento do chunk (13), tipo "IHDR", depois largura e altura.
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8, 4), 13);
    "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), largura);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), altura);
    return bytes;
  }
}

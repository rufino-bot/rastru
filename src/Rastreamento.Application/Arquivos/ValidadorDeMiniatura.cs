using System.Buffers.Binary;

namespace Rastreamento.Application.Arquivos;

/// <summary>
/// Valida a miniatura PNG do solido de um Componente: nao vazia, ate 512 KiB, assinatura PNG e
/// dimensao exata de 320 x 240 lida do chunk IHDR. Nao decodifica a imagem: o navegador que a
/// gerou e o unico produtor esperado, e a checagem barata basta para recusar arquivo errado.
///
/// <para>Puro de proposito, no molde de <see cref="ValidadorDeArquivoStl"/>: sem banco nem I/O.</para>
/// </summary>
public static class ValidadorDeMiniatura
{
  public const int Largura = 320;

  public const int Altura = 240;

  public const int TamanhoMaximoEmBytes = 512 * 1024;

  // Assinatura (8) + comprimento do chunk (4) + "IHDR" (4) + largura (4) + altura (4).
  private const int OffsetDoIhdr = 12;
  private const int OffsetDaLargura = 16;
  private const int OffsetDaAltura = 20;
  private const int TamanhoMinimoEmBytes = 24;

  private static readonly byte[] AssinaturaPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

  private const string ErroDeVazio = "A miniatura esta vazia.";
  private const string ErroDeTamanho = "A miniatura passa de 512 KiB.";
  private const string ErroDeFormato = "A miniatura nao e um PNG.";
  private const string ErroDeDimensao = "A miniatura precisa ter 320 x 240 pixels.";

  /// <summary>Mensagem do primeiro problema, ou null se a miniatura for valida.</summary>
  public static string? Validar(byte[] conteudo)
  {
    if (conteudo.Length == 0) return ErroDeVazio;
    if (conteudo.Length > TamanhoMaximoEmBytes) return ErroDeTamanho;

    if (conteudo.Length < TamanhoMinimoEmBytes
        || !conteudo.AsSpan(0, AssinaturaPng.Length).SequenceEqual(AssinaturaPng)
        || !conteudo.AsSpan(OffsetDoIhdr, 4).SequenceEqual("IHDR"u8))
      return ErroDeFormato;

    var largura = BinaryPrimitives.ReadUInt32BigEndian(conteudo.AsSpan(OffsetDaLargura, 4));
    var altura = BinaryPrimitives.ReadUInt32BigEndian(conteudo.AsSpan(OffsetDaAltura, 4));
    return largura == Largura && altura == Altura ? null : ErroDeDimensao;
  }
}

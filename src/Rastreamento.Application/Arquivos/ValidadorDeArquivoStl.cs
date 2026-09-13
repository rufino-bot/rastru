using System.Text;

namespace Rastreamento.Application.Arquivos;

/// <summary>
/// Valida um arquivo de solido em tres camadas: extensao, tamanho e ESTRUTURA. A terceira e a que
/// importa — sem ela um PDF renomeado para .stl sobe, e o defeito so aparece no viewer, longe da
/// causa.
///
/// <para>
/// Puro de proposito: sem banco, sem I/O, sem <c>IFormFile</c>. Assim a camada mais sujeita a
/// arquivo real estranho e a mais barata de testar.
/// </para>
/// </summary>
public static class ValidadorDeArquivoStl
{
  /// <summary>
  /// 16 MiB. Raciocinio: /50 bytes por triangulo = ~335 mil triangulos, malha fina de sobra para
  /// peca de metalurgia, e cabe no limite default do corpo de requisicao do Kestrel (30.000.000
  /// bytes). A folga e pequena — dobrar este numero ultrapassa o teto do Kestrel, e ai deixa de
  /// ser constante e passa a ser configuracao de pipeline.
  /// </summary>
  public const int TamanhoMaximoEmBytes = 16 * 1024 * 1024;

  private const int CabecalhoBinarioEmBytes = 80;

  private const int ContagemEmBytes = 4;

  private const int BytesPorTriangulo = 50;

  private const string ErroDeExtensao = "O arquivo do solido deve ter extensao .stl.";

  private const string ErroDeArquivoVazio = "O arquivo esta vazio.";

  private static readonly string ErroDeTamanho =
      $"O arquivo passa de 16 MiB ({TamanhoMaximoEmBytes} bytes).";

  private const string ErroDeEstrutura =
      "O arquivo nao e um STL valido (nem binario nem ASCII).";

  /// <summary>
  /// Mensagem do primeiro problema, ou null se estiver valido. Molde de <c>Validar</c> em
  /// <c>CadastroDeComponenteUseCase</c>.
  /// </summary>
  public static string? Validar(string nomeOriginal, byte[] conteudo)
  {
    if (!nomeOriginal.EndsWith(".stl", StringComparison.OrdinalIgnoreCase))
      return ErroDeExtensao;

    if (conteudo.Length == 0) return ErroDeArquivoVazio;
    if (conteudo.Length > TamanhoMaximoEmBytes) return ErroDeTamanho;

    // BINARIO PRIMEIRO, e a ordem e a regra, nao preferencia: ha STL binario de verdade cujos 80
    // bytes de cabecalho comecam com "solid" (o exportador escreve um texto livre ali). Testando
    // ASCII primeiro, esse arquivo seria lido como ASCII e a formula 84+50n nunca seria conferida.
    if (EhBinarioCoerente(conteudo)) return null;
    if (EhAsciiCoerente(conteudo)) return null;

    return ErroDeEstrutura;
  }

  /// <summary>
  /// 80 bytes de cabecalho + uint32 little-endian com a contagem + 50 bytes por triangulo. Logo o
  /// arquivo coerente tem EXATAMENTE 84 + 50n bytes, com n lido do proprio arquivo.
  /// </summary>
  private static bool EhBinarioCoerente(byte[] conteudo)
  {
    if (conteudo.Length < CabecalhoBinarioEmBytes + ContagemEmBytes) return false;

    var triangulos = BitConverter.ToUInt32(conteudo, CabecalhoBinarioEmBytes);
    // Zero triangulo e estruturalmente coerente e nao e solido nenhum — recusa aqui, nao adiante.
    if (triangulos == 0) return false;

    // Em long para a multiplicacao nao estourar antes da comparacao: `triangulos` e uint32.
    var esperado = (long)CabecalhoBinarioEmBytes + ContagemEmBytes + (long)triangulos * BytesPorTriangulo;
    return conteudo.Length == esperado;
  }

  private static bool EhAsciiCoerente(byte[] conteudo)
  {
    // Le so o comeco: um ASCII de 16 MiB nao precisa virar string inteira para provar a forma.
    var amostra = Encoding.UTF8.GetString(
        conteudo, 0, Math.Min(conteudo.Length, 4096));

    return amostra.TrimStart().StartsWith("solid", StringComparison.OrdinalIgnoreCase)
        && amostra.Contains("facet normal", StringComparison.OrdinalIgnoreCase);
  }
}

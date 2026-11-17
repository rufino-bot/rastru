namespace Rastreamento.Application.Arquivos;

/// <summary>
/// O solido pronto para o controller responder. NAO carrega Id nem hash: o cliente identifica o
/// recurso pelo id do COMPONENTE (e por ele que a rota pergunta), e expor um id de arquivo abriria
/// um segundo caminho para o mesmo recurso.
/// </summary>
public sealed record ArquivoDeSolidoDto(string NomeOriginal, byte[] Conteudo);

/// <summary>
/// A miniatura pronta para o controller responder. <c>Versao</c> e o <c>Sha256</c> do PNG em
/// hexadecimal minusculo (ver <see cref="VersaoDeArquivo.VersaoDe"/>): e so versao, nao endereco --
/// o mesmo motivo de <see cref="ArquivoDeSolidoDto"/> nao carregar o id do arquivo.
/// </summary>
public sealed record ArquivoDeMiniaturaDto(byte[] Conteudo, string Versao);

public static class VersaoDeArquivo
{
  /// <summary>A versao de um arquivo: o <c>Sha256</c> dele em hexadecimal minusculo (64 digitos).</summary>
  public static string VersaoDe(byte[] sha256) => Convert.ToHexStringLower(sha256);
}

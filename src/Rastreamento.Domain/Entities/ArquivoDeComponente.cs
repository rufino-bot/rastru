namespace Rastreamento.Domain.Entities;

/// <summary>
/// Blob de um arquivo de Componente. Tabela SEPARADA de Componente de proposito: o catalogo e
/// listado paginado, e um VARBINARY(MAX) na mesma linha convidaria a arrastar megabytes numa
/// listagem. Hoje so o solido (STL) tem consumidor; a foto entra depois pela mesma tabela.
/// </summary>
public class ArquivoDeComponente
{
  public int Id { get; set; }

  /// <summary>O nome que o usuario subiu: exibicao na tela e Content-Disposition do download.</summary>
  public string NomeOriginal { get; set; } = string.Empty;

  public byte[] Conteudo { get; set; } = [];

  /// <summary>
  /// Redundante com <c>Conteudo.Length</c> de proposito: permite exibir o tamanho sem SELECT no
  /// blob.
  /// </summary>
  public int TamanhoEmBytes { get; set; }

  public byte[] Sha256 { get; set; } = [];

  /// <summary>Vem do DEFAULT do banco (Database First), nao do C#.</summary>
  public DateTime CriadoEm { get; set; }

  public int CriadoPorUsuarioId { get; set; }
}

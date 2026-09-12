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
  /// CALCULADA PELO BANCO (emenda de 2026-09-12, apos review da Task 2): coluna computada
  /// <c>PERSISTED</c> sobre <c>DATALENGTH(Conteudo)</c> -- ninguem em C# preenche isto. O `set`
  /// continua publico porque o EF Core precisa dele para materializar a entidade ao ler; atribuir
  /// um valor aqui antes de <c>SaveChanges</c> nao tem efeito nenhum sobre o que fica gravado, e o
  /// SQL Server recusa qualquer INSERT/UPDATE que tente escrever na coluna (Msg 271). O motivo de
  /// ser calculada, e nao redundante-por-disciplina: o invariante `TamanhoEmBytes ==
  /// Conteudo.Length` fica com dono e guarda, em vez de depender do caso de uso acertar sempre.
  /// </summary>
  public int TamanhoEmBytes { get; set; }

  /// <summary>
  /// CALCULADA PELO BANCO, mesmo mecanismo de <see cref="TamanhoEmBytes"/>: coluna computada
  /// <c>PERSISTED</c> sobre <c>HASHBYTES('SHA2_256', Conteudo)</c>, com <c>CAST</c> para
  /// <c>BINARY(32)</c> -- sem o CAST o SQL Server usa <c>VARBINARY(8000)</c> (medido na bancada).
  /// </summary>
  public byte[] Sha256 { get; set; } = [];

  /// <summary>Vem do DEFAULT do banco (Database First), nao do C#.</summary>
  public DateTime CriadoEm { get; set; }

  public int CriadoPorUsuarioId { get; set; }
}

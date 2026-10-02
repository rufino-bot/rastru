namespace Rastreamento.Domain.Entities;

/// <summary>
/// Um registro por CODIGO distinto lido do BOM (a receita e por codigo, nao por ocorrencia -- spec do
/// import, secao 3). <see cref="CodigoLido"/> nulo e a linha sem part number, e cada uma vira um
/// registro proprio. Casado com o catalogo (<see cref="ComponenteId"/>) ou "criar novo"
/// (<see cref="CodigoNovo"/>, <see cref="DescricaoNova"/>, <see cref="TipoNovo"/>), nunca os dois.
/// </summary>
public class ImportacaoDeEstruturaComponente
{
  public int Id { get; set; }
  public int ImportacaoId { get; set; }
  public string? CodigoLido { get; set; }
  public string DescricaoLida { get; set; } = string.Empty;
  public int? ComponenteId { get; set; }
  public string? CodigoNovo { get; set; }
  public string? DescricaoNova { get; set; }

  /// <summary>Bruto | Fabricado | Montagem, o dominio de <c>Componente.Tipo</c>.</summary>
  public string? TipoNovo { get; set; }

  /// <summary>Catalogo | Importada | nulo (ainda nao decidido).</summary>
  public string? EscolhaDeReceita { get; set; }

  /// <summary>Hash (SHA-256) da receita de catalogo vista no momento da escolha.</summary>
  public byte[]? ImpressaoDaReceitaDoCatalogo { get; set; }

  /// <summary>
  /// O solido (STL) pendente, em <c>ArquivoDeComponente</c>, sem Componente apontando para ele ate a
  /// confirmacao. Descartar o rascunho o apaga, pela aplicacao.
  /// </summary>
  public int? ArquivoSolidoPendenteId { get; set; }

  /// <summary>As linhas em que este registro e o PAI: a receita lida, um nivel.</summary>
  public List<ImportacaoDeEstruturaFilho> Filhos { get; set; } = [];
}

namespace Rastreamento.Domain.Entities;

/// <summary>
/// Uma aresta pai -> filho da receita lida (um nivel). <see cref="Quantidade"/> e a corrigida na
/// conferencia e nasce igual a <see cref="QuantidadeLida"/>.
/// </summary>
public class ImportacaoDeEstruturaFilho
{
  public int Id { get; set; }
  public int PaiId { get; set; }
  public int FilhoId { get; set; }

  /// <summary>
  /// Quem monta o rascunho ainda nao conhece o Id do filho: aponta o objeto aqui e o EF resolve
  /// <see cref="FilhoId"/> na gravacao. Nao e carregada ao ler do banco -- leitura usa
  /// <see cref="FilhoId"/>.
  /// </summary>
  public ImportacaoDeEstruturaComponente? Filho { get; set; }

  public int Ordem { get; set; }
  public decimal QuantidadeLida { get; set; }
  public decimal Quantidade { get; set; }
}

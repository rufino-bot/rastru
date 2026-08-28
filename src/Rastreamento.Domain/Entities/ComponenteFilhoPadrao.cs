namespace Rastreamento.Domain.Entities;

/// <summary>
/// Uma linha da receita padrao de FILHOS: "o componente Pai leva N unidades do componente Filho".
/// Sem propriedade de navegacao de proposito — o resto do projeto tambem mapeia so as FKs, e
/// navegacao aqui convidaria a carregar o grafo inteiro por acidente.
/// </summary>
public class ComponenteFilhoPadrao
{
  public int Id { get; set; }
  public int ComponentePaiId { get; set; }
  public int ComponenteFilhoId { get; set; }
  public decimal QuantidadePadrao { get; set; }
}

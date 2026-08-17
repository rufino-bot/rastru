namespace Rastreamento.Domain.Entities;

/// <summary>Uma linha da receita padrao de MATERIAIS: "o Componente consome N do Material".</summary>
public class ComponenteMaterialPadrao
{
  public int Id { get; set; }
  public int ComponenteId { get; set; }
  public int MaterialId { get; set; }
  public decimal QuantidadePadrao { get; set; }
}

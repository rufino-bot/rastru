namespace Rastreamento.Domain.Entities;

/// <summary>Material de um no da arvore. Copiado de `ComponenteMaterialPadrao` na criacao.</summary>
public class EstruturaMaterial
{
  public int Id { get; set; }
  public int EstruturaItemId { get; set; }
  public int MaterialId { get; set; }
  public decimal Quantidade { get; set; }
}

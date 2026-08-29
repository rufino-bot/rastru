namespace Rastreamento.Domain.Entities;

/// <summary>
/// Passo de setor de um no. Copiado de `ComponenteRoteiroPadrao`, com a `Ordem` PRESERVADA:
/// setor repetido e RETORNO ao mesmo setor, nao duplicata (regra 21), e a unicidade do schema e
/// (EstruturaItemId, Ordem) — da posicao, nao do Setor. Reindexar a Ordem na copia perderia o
/// retorno.
/// </summary>
public class EstruturaRoteiro
{
  public int Id { get; set; }
  public int EstruturaItemId { get; set; }
  public int SetorId { get; set; }
  public int Ordem { get; set; }
}

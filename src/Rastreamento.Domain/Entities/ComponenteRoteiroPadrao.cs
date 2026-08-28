namespace Rastreamento.Domain.Entities;

/// <summary>
/// Um passo do roteiro padrao: "o Componente passa pelo Setor na posicao Ordem".
/// `Ordem` e 1-based e QUEM A ATRIBUI E O CASO DE USO, nunca o cliente — ver Task 4.
/// O mesmo Setor pode aparecer mais de uma vez (retorno ao setor): o UQ e (ComponenteId, Ordem).
/// </summary>
public class ComponenteRoteiroPadrao
{
  public int Id { get; set; }
  public int ComponenteId { get; set; }
  public int SetorId { get; set; }
  public int Ordem { get; set; }
}

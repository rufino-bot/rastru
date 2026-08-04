namespace Rastreamento.Domain.Entities;

public class Setor
{
  public int Id { get; set; }
  public string Nome { get; set; } = string.Empty;

  /// <summary>Catalogo nao se exclui, se inativa: linhas de historico apontam para o Setor.</summary>
  public bool Ativo { get; set; }
}

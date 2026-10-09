namespace Rastreamento.Domain.Entities;

public class Setor
{
  public int Id { get; set; }
  public string Nome { get; set; } = string.Empty;

  /// <summary>Catalogo nao se exclui, se inativa: linhas de historico apontam para o Setor.</summary>
  public bool Ativo { get; set; }

  /// <summary>
  /// Substantivo que da nome aos botoes da fila ("montagem" -> "Iniciar montagem"). Nulo: os botoes
  /// ficam "Iniciar" e "Terminar" (spec da Fase 3D, secao 2.3).
  /// </summary>
  public string? Atividade { get; set; }

  /// <summary>
  /// Regra 25: o Setor onde o Kit e montado. Filho de Agrupamento Kit que vai a montagem do pai num Setor
  /// marcado so entra em conjunto completo (spec da Fase 3B, D3). Sem HasDefaultValue no EF: o default vive no .sql.
  /// </summary>
  public bool UtilizaKit { get; set; }
}

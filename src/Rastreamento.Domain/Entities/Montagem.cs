namespace Rastreamento.Domain.Entities;

/// <summary>
/// A montagem de um no com filhos (regra 24): nasce quando o operador INICIA o no no primeiro passo
/// do Roteiro (Fase 3D), consumindo os filhos. O total montado do no e a soma das montagens nao
/// estornadas. A baixa de cada filho mora em `Movimentacao` (Tipo = Montagem, `MontagemId` = esta),
/// com `N x QuantidadePorPai` gravado, e o Inicio do pai tambem aponta esta linha. Estornar e marcar
/// `EstornadaEm`/`EstornadaPorUsuarioId` — a unica escrita que uma linha desta tabela recebe depois de
/// nascer — e gravar um Estorno por baixa e, quando ha Inicio do pai, um Estorno dele tambem (montagem
/// anterior a Fase 3D nao tem Inicio apontando para ela: so as baixas sao estornadas).
/// </summary>
public class Montagem
{
  public int Id { get; set; }

  /// <summary>O pai montado.</summary>
  public int EstruturaItemId { get; set; }

  public int SetorId { get; set; }
  public decimal Quantidade { get; set; }
  public DateTime DataHora { get; set; }
  public int UsuarioId { get; set; }
  public DateTime? EstornadaEm { get; set; }
  public int? EstornadaPorUsuarioId { get; set; }
}

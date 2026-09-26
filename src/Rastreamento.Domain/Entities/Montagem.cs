namespace Rastreamento.Domain.Entities;

/// <summary>
/// "Montei N" de um no com filhos (regra 24). O total montado do no e a soma das montagens nao
/// estornadas. A baixa de cada filho mora em `Movimentacao` (Tipo = Montagem, `MontagemId` = esta),
/// com `N x QuantidadePorPai` gravado. Estornar e marcar `EstornadaEm`/`EstornadaPorUsuarioId` — a
/// unica escrita que uma linha desta tabela recebe depois de nascer — e gravar um Estorno por baixa.
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

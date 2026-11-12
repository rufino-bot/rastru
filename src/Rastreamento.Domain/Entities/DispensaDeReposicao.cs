namespace Rastreamento.Domain.Entities;

/// <summary>
/// O "nao repor" (D3 da spec da Fase 5B-2, secao 3.4): a justificativa de quem decidiu nao repor uma baixa por perda
/// ou uma reclamacao. Exatamente um alvo (`CK_DispensaDeReposicao_Alvo`), e no maximo uma dispensa vigente por alvo
/// (indices unicos filtrados). SO INCLUSAO: desfazer marca `EstornadaEm`/`EstornadaPorUsuarioId`.
/// </summary>
public class DispensaDeReposicao
{
  public int Id { get; set; }
  public int? PerdaId { get; set; }
  public int? ReclamacaoId { get; set; }
  public string Justificativa { get; set; } = string.Empty;

  /// <summary>UTC, preenchido pelo caso de uso (o DEFAULT do banco e rede, nao contrato).</summary>
  public DateTime DataHora { get; set; }

  public int UsuarioId { get; set; }
  public DateTime? EstornadaEm { get; set; }
  public int? EstornadaPorUsuarioId { get; set; }
}

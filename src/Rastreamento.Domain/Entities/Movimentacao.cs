namespace Rastreamento.Domain.Entities;

/// <summary>
/// Uma linha do livro de movimentacoes: move `Quantidade` de um no de uma posicao para outra (spec da
/// Fase 3, secao 3.3). SO INCLUSAO — nenhum codigo edita nem apaga uma linha; correcao e um Estorno
/// que aponta o original (`EstornoDeId`). Setor e passo (`Ordem` do Roteiro do proprio no) so existem
/// nas posicoes que os tem; os CHECKs do banco garantem a coerencia.
/// </summary>
public class Movimentacao
{
  public int Id { get; set; }
  public int EstruturaItemId { get; set; }
  public string Tipo { get; set; } = string.Empty;
  public decimal Quantidade { get; set; }
  public string OrigemPosicao { get; set; } = string.Empty;
  public int? OrigemSetorId { get; set; }
  public int? OrigemOrdem { get; set; }
  public string DestinoPosicao { get; set; } = string.Empty;
  public int? DestinoSetorId { get; set; }
  public int? DestinoOrdem { get; set; }

  /// <summary>So na baixa de filho de uma montagem, e no estorno dela.</summary>
  public int? MontagemId { get; set; }

  /// <summary>
  /// So na `Medicao` (e no estorno dela): a contagem a que a linha pertence. Uma contagem sao duas linhas
  /// `Medicao`, e estorna-se a contagem inteira (C17 da spec da Fase 5A).
  /// </summary>
  public int? ContagemId { get; set; }

  /// <summary>So em `PedidoDeLiberacao`, `Liberacao` e `Recusa` (e no estorno delas): o registro da liberacao.</summary>
  public int? LiberacaoId { get; set; }

  /// <summary>So na `Expedicao` (e no estorno dela): a remessa. Estorna-se a remessa inteira (C8).</summary>
  public int? RemessaId { get; set; }

  /// <summary>So no Estorno: o movimento que ele desfaz (`UX_Movimentacao_EstornoDe`: uma vez so).</summary>
  public int? EstornoDeId { get; set; }

  /// <summary>UTC, preenchido pelo caso de uso (o DEFAULT do banco e rede, nao contrato).</summary>
  public DateTime DataHora { get; set; }

  /// <summary>O autor — quem pode estornar sem ser PCP ou Administrador (spec secao 4.5).</summary>
  public int UsuarioId { get; set; }
}

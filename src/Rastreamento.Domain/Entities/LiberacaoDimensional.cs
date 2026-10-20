namespace Rastreamento.Domain.Entities;

/// <summary>
/// O registro da liberacao de unidades nao conformes (D7, C5 e C6 da spec da Fase 5A, secao 3.6): o PEDIDO
/// da Qualidade (a quantidade pedida e a justificativa) e a RESPOSTA da Gestao (a quantidade liberada, de
/// zero ate a pedida, e a observacao opcional), na mesma tabela, distinguidos por `Tipo`. SO INCLUSAO —
/// estornar e estornar as linhas do livro que apontam o registro (`Movimentacao.LiberacaoId`), nunca editar este.
/// </summary>
public class LiberacaoDimensional
{
  public int Id { get; set; }
  public int EstruturaItemId { get; set; }

  /// <summary>Um dos <see cref="TiposDeLiberacao"/> (`CK_LiberacaoDimensional_Tipo`).</summary>
  public string Tipo { get; set; } = string.Empty;

  /// <summary>So na Resposta: o pedido que ela responde. No Pedido e nulo.</summary>
  public int? PedidoDeLiberacaoId { get; set; }

  /// <summary>No Pedido, a quantidade pedida (positiva); na Resposta, a liberada (zero ou mais).</summary>
  public decimal Quantidade { get; set; }

  /// <summary>No Pedido, a justificativa (obrigatoria); na Resposta, a observacao (opcional).</summary>
  public string? Texto { get; set; }

  /// <summary>UTC, preenchido pelo caso de uso (o DEFAULT do banco e rede, nao contrato).</summary>
  public DateTime DataHora { get; set; }

  /// <summary>O autor: a Qualidade no Pedido, a Gestao na Resposta.</summary>
  public int UsuarioId { get; set; }
}

/// <summary>Os tipos de `CK_LiberacaoDimensional_Tipo`.</summary>
public static class TiposDeLiberacao
{
  public const string Pedido = "Pedido";
  public const string Resposta = "Resposta";
}

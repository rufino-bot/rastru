namespace Rastreamento.Domain.Entities;

/// <summary>
/// O agrupador da baixa (spec da Fase 5B-1, secao 3.2): por que, de que escopo e quem. SO INCLUSAO, como a
/// `Remessa`. As quantidades sao as linhas `Perda` do livro que apontam a baixa (`Movimentacao.PerdaId`), uma
/// por no e por posicao de origem; estorna-se a baixa inteira, e "estornada" e sempre calculado.
/// </summary>
public class Perda
{
  public int Id { get; set; }

  /// <summary>Um de <see cref="MotivosDePerda"/> (`CK_Perda_Motivo`).</summary>
  public string Motivo { get; set; } = string.Empty;

  /// <summary>Livre e opcional, ate 500 caracteres (C19).</summary>
  public string? Observacao { get; set; }

  /// <summary>Toda baixa e de um Pedido so.</summary>
  public int PedidoId { get; set; }

  /// <summary>So no "Descartar tudo" do Agrupamento. No maximo um de <c>AgrupamentoId</c> e <c>EstruturaItemId</c> (`CK_Perda_Escopo`).</summary>
  public int? AgrupamentoId { get; set; }

  /// <summary>So na baixa de um no: o no em que se clicou (as linhas podem ser dele, de descendentes e, por D13, da Peca do topo).</summary>
  public int? EstruturaItemId { get; set; }

  /// <summary>A baixa levou tudo (D11): o "tudo" de um no, ou o "Descartar tudo". Marca "saiu do projeto" (C22).</summary>
  public bool Tudo { get; set; }

  /// <summary>UTC, preenchido pelo caso de uso (o DEFAULT do banco e rede, nao contrato).</summary>
  public DateTime DataHora { get; set; }

  /// <summary>O autor: o usuario logado.</summary>
  public int UsuarioId { get; set; }

  /// <summary>
  /// O Pedido de Retrabalho (secao 3.2 da spec da Fase 5B-2): na baixa de `PerdaArmazem` ou `MortaEmProcesso`, o que a
  /// repoe; na de `Reaproveitado`, o que recebeu a sobra. Nunca na de `Descarte` (`CK_Perda_DescarteSemRetrabalho`);
  /// obrigatoria na `Reaproveitado` (`CK_Perda_ReaproveitadoTemRetrabalho`).
  /// </summary>
  public int? PedidoRetrabalhoId { get; set; }

  /// <summary>
  /// So na baixa ligada da cascata do pronto (decisao P2 do plano de backend da Fase 5B-2): a baixa `Reaproveitado`
  /// do original a que ela pertence. Uma `Perda` e de um Pedido so, por isso a cascata e uma baixa a parte.
  /// </summary>
  public int? PerdaDeOrigemId { get; set; }
}

/// <summary>Os motivos de `CK_Perda_Motivo`.</summary>
public static class MotivosDePerda
{
  public const string PerdaArmazem = "PerdaArmazem";
  public const string MortaEmProcesso = "MortaEmProcesso";
  public const string Descarte = "Descarte";

  /// <summary>So nasce pelo "Usar a sobra" (C4 da spec da Fase 5B-2): o "Dar baixa" o recusa.</summary>
  public const string Reaproveitado = "Reaproveitado";

  /// <summary>Os quatro de `CK_Perda_Motivo`.</summary>
  public static readonly IReadOnlyList<string> Todos = [PerdaArmazem, MortaEmProcesso, Descarte, Reaproveitado];

  /// <summary>Os tres que o "Dar baixa" aceita (os da Fase 5B-1).</summary>
  public static readonly IReadOnlyList<string> DaBaixa = [PerdaArmazem, MortaEmProcesso, Descarte];

  /// <summary>
  /// As baixas que tiram Peca do cliente e por isso se repoem (C1 da spec da Fase 5B-2, regra 39): `Descarte` e
  /// `Reaproveitado` nao. E a unica copia deste conjunto — a abertura do Retrabalho, o "nao repor", a contagem do
  /// "a repor", o estorno da baixa e as duas consultas do repositorio de reposicao usam esta lista.
  /// </summary>
  public static readonly IReadOnlyList<string> QueSeRepoem = [PerdaArmazem, MortaEmProcesso];
}

namespace Rastreamento.Domain.Entities;

/// <summary>
/// As posicoes da quantidade de um no (spec da Fase 3, secao 3.1). Os valores sao os de
/// `CK_Movimentacao_OrigemPosicao` e `CK_Movimentacao_DestinoPosicao` — mudar um lado sem o outro faz o
/// banco recusar. Nome no plural para nao colidir com a propriedade `Posicao` dos records da
/// Application.
/// </summary>
public static class Posicoes
{
  public const string AIniciar = "AIniciar";
  public const string NoSetor = "NoSetor";
  public const string AguardandoColeta = "AguardandoColeta";
  public const string AguardandoMontagem = "AguardandoMontagem";
  public const string NaExpedicao = "NaExpedicao";
  public const string Montado = "Montado";

  /// <summary>A ordem em que as telas listam as posicoes de um no.</summary>
  public static readonly IReadOnlyList<string> Todas =
      [AIniciar, NoSetor, AguardandoColeta, AguardandoMontagem, NaExpedicao, Montado];
}

/// <summary>Os tipos de `CK_Movimentacao_Tipo`. A Fase 5 acrescenta `Pronto`.</summary>
public static class TiposDeMovimentacao
{
  public const string Inicio = "Inicio";
  public const string Termino = "Termino";
  public const string Entrega = "Entrega";
  public const string Montagem = "Montagem";
  public const string Estorno = "Estorno";
}

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

  // Fim do caminho da Peca (spec da Fase 5A, secao 3.1). Os nomes cabem no NVARCHAR(20) das colunas de
  // posicao (o maior, `AguardandoLiberacao`, tem 19); quem renomear confere.
  public const string AguardandoConserto = "AguardandoConserto";
  public const string EmConserto = "EmConserto";
  public const string ConsertoPronto = "ConsertoPronto";
  public const string Conforme = "Conforme";
  public const string NaoConforme = "NaoConforme";
  public const string AguardandoLiberacao = "AguardandoLiberacao";
  public const string Liberada = "Liberada";
  public const string Expedido = "Expedido";

  /// <summary>A ordem em que as telas listam as posicoes de um no.</summary>
  public static readonly IReadOnlyList<string> Todas =
  [
    AIniciar, NoSetor, AguardandoColeta, AguardandoMontagem, AguardandoConserto, EmConserto, ConsertoPronto,
    NaExpedicao, Conforme, NaoConforme, AguardandoLiberacao, Liberada, Montado, Expedido,
  ];

  /// <summary>
  /// As cinco posicoes do "no local de expedicao" da regra 9: a unidade ja esta la, e o que ela e depende da
  /// afericao da Peca (secao 3.2 da spec da Fase 5A).
  /// </summary>
  public static readonly IReadOnlySet<string> NoLocalDeExpedicao =
      new HashSet<string> { NaExpedicao, Conforme, NaoConforme, AguardandoLiberacao, Liberada };

  /// <summary>As tres posicoes do conserto: contam como em producao, e as tres tem Setor e nenhum passo.</summary>
  public static readonly IReadOnlySet<string> DoConserto =
      new HashSet<string> { AguardandoConserto, EmConserto, ConsertoPronto };

  /// <summary>
  /// O que so existe depois que a Peca chega ao fim do Roteiro: o local de expedicao, o conserto e
  /// `Expedido`. Nove posicoes, e e por elas que o estorno e as leituras reconhecem uma linha do fim do caminho.
  /// </summary>
  public static readonly IReadOnlySet<string> DoFimDoCaminho =
      new HashSet<string>(NoLocalDeExpedicao.Concat(DoConserto)) { Expedido };

  /// <summary>A linha toca o fim do caminho quando sai de uma posicao dele ou chega a uma.</summary>
  public static bool TocaOFimDoCaminho(Movimentacao m) =>
      DoFimDoCaminho.Contains(m.OrigemPosicao) || DoFimDoCaminho.Contains(m.DestinoPosicao);
}

/// <summary>Os tipos de `CK_Movimentacao_Tipo`. O `Pronto` e da Fase 5B.</summary>
public static class TiposDeMovimentacao
{
  public const string Inicio = "Inicio";
  public const string Termino = "Termino";
  public const string Entrega = "Entrega";
  public const string Montagem = "Montagem";

  // Fase 5A (secao 3.3 da spec): aferir e expedir mexem na quantidade do no, entao entram no livro.
  public const string Medicao = "Medicao";
  public const string Conserto = "Conserto";
  public const string PedidoDeLiberacao = "PedidoDeLiberacao";
  public const string Liberacao = "Liberacao";
  public const string Recusa = "Recusa";
  public const string Expedicao = "Expedicao";
  public const string Estorno = "Estorno";
}

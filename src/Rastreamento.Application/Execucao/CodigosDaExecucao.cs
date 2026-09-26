namespace Rastreamento.Application.Execucao;

/// <summary>
/// Os codigos estaveis do campo `erro` da Fase 3 (spec secao 8.2; `OrigemInvalida` e o desvio D3 do
/// plano 2). O front comuta por eles; a frase para o operador vai em `Result.Detalhe`.
/// </summary>
public static class CodigosDaExecucao
{
  public const string QuantidadeInvalida = "QuantidadeInvalida";
  public const string DestinoIndevido = "DestinoIndevido";
  public const string EntregaVazia = "EntregaVazia";
  public const string RoteiroInvalido = "RoteiroInvalido";
  public const string OrigemInvalida = "OrigemInvalida";
  public const string Proibido = "Proibido";
  public const string SemRoteiro = "SemRoteiro";
  public const string NaoEhOPrimeiroPasso = "NaoEhOPrimeiroPasso";
  public const string SaldoInsuficiente = "SaldoInsuficiente";
  public const string SemFilhos = "SemFilhos";
  public const string MontagemAcimaDoQueFalta = "MontagemAcimaDoQueFalta";
  public const string FilhosInsuficientes = "FilhosInsuficientes";
  public const string DestinoForaDoRoteiroDoPai = "DestinoForaDoRoteiroDoPai";
  public const string PaiSemRoteiro = "PaiSemRoteiro";
  public const string PassoJaAlcancado = "PassoJaAlcancado";
  public const string QuantidadeAbaixoDoMovimentado = "QuantidadeAbaixoDoMovimentado";
  public const string EstornoImpossivel = "EstornoImpossivel";
  public const string JaEstornado = "JaEstornado";
  public const string PedidoFechado = "PedidoFechado";
  public const string ConflitoDeConcorrencia = "ConflitoDeConcorrencia";

  public const string MensagemDeConflito =
      "Outra pessoa registrou neste item ao mesmo tempo; atualize e tente de novo.";
}

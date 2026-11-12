namespace Rastreamento.Application.Retrabalho;

/// <summary>
/// Os codigos estaveis do campo `erro` da reposicao (contrato JSON da Fase 5B-2). O front comuta por
/// eles; a frase para o operador vai em `Result.Detalhe`. Os codigos de outras fases que a reposicao
/// reusa (`SaldoInsuficiente`, `PedidoFechado`, `JaEstornado`...) ficam em `CodigosDaExecucao`.
/// </summary>
public static class CodigosDoRetrabalho
{
  // 409
  public const string JaReposto = "JaReposto";
  public const string ReposicaoDispensada = "ReposicaoDispensada";
  public const string SemPecaARepor = "SemPecaARepor";
  public const string ReposicaoResolvida = "ReposicaoResolvida";
  public const string AbaixoDoReclamado = "AbaixoDoReclamado";
  public const string AlemDaSobra = "AlemDaSobra";
  public const string ProntoJaAndou = "ProntoJaAndou";
  public const string RetrabalhoForaDeAberto = "RetrabalhoForaDeAberto";

  // 400
  public const string PosicaoDoProntoInexistente = "PosicaoDoProntoInexistente";
  public const string DestinoInvalido = "DestinoInvalido";
  public const string PortaInvalida = "PortaInvalida";
  public const string DescricaoObrigatoria = "DescricaoObrigatoria";
  public const string NumeroObrigatorio = "NumeroObrigatorio";
  public const string NumeroLongoDemais = "NumeroLongoDemais";
}

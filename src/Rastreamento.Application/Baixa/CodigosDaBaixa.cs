using Rastreamento.Application.Execucao;

namespace Rastreamento.Application.Baixa;

/// <summary>
/// Os codigos estaveis do campo `erro` da baixa (secao 5.4 da spec da Fase 5B-1). Os reusados ficam onde nasceram:
/// `ItemRepetido`, `SaldoInsuficiente`, `QuantidadeInvalida`, `PedidoFechado`, `JaEstornado` e os do lote em
/// `CodigosDaExecucao`; `TextoLongoDemais` e `LiberacaoEmAberto` em `CodigosDaExpedicao`. O front comuta por eles; a
/// frase vai em `Result.Detalhe`.
/// </summary>
public static class CodigosDaBaixa
{
  public const string MotivoInvalido = nameof(MotivoInvalido);

  /// <summary>O mesmo texto do da execucao: e a mesma recusa, posicao que nao serve para esta acao.</summary>
  public const string OrigemInvalida = CodigosDaExecucao.OrigemInvalida;

  public const string NadaEmProducao = nameof(NadaEmProducao);
  public const string DiminuirPelaBaixa = nameof(DiminuirPelaBaixa);
}

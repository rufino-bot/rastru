namespace Rastreamento.Application.Expedicao;

/// <summary>
/// Os codigos estaveis do campo `erro` da afericao, da contagem, do conserto, da liberacao e da remessa
/// (secao 5.3 da spec da Fase 5A). Os reusados da execucao (`QuantidadeInvalida`, `SaldoInsuficiente`,
/// `EstornoImpossivel`, `JaEstornado`, `PedidoFechado`, `Proibido`, os do lote) ficam em `CodigosDaExecucao`.
/// O front comuta por eles; a frase vai em `Result.Detalhe`.
/// </summary>
public static class CodigosDaExpedicao
{
  public const string NaoEhPeca = nameof(NaoEhPeca);
  public const string SemRelatorioDimensional = nameof(SemRelatorioDimensional);
  public const string ResultadoInvalido = nameof(ResultadoInvalido);
  public const string AfericaoJaRegistrada = nameof(AfericaoJaRegistrada);
  public const string SemAfericaoNaoConforme = nameof(SemAfericaoNaoConforme);
  public const string ContagemVazia = nameof(ContagemVazia);
  public const string SetorInvalido = nameof(SetorInvalido);
  public const string JustificativaObrigatoria = nameof(JustificativaObrigatoria);
  public const string TextoLongoDemais = nameof(TextoLongoDemais);
  public const string LiberacaoEmAberto = nameof(LiberacaoEmAberto);
  public const string LiberacaoNaoPendente = nameof(LiberacaoNaoPendente);
  public const string QuantidadeAcimaDaPedida = nameof(QuantidadeAcimaDaPedida);
  public const string ComAfericao = nameof(ComAfericao);

  /// <summary>O teto da justificativa do pedido de liberacao e da observacao da resposta (C6).</summary>
  public const int TamanhoMaximoDoTexto = 500;
}

namespace Rastreamento.Application.Panorama;

/// <summary>
/// Os codigos estaveis do campo `erro` do "Embalado". Os reusados (`NaoEhPeca` da expedicao, `PedidoFechado` da
/// execucao) ficam onde ja estao. O front comuta por eles; a frase vai em `Result.Detalhe`.
/// </summary>
public static class CodigosDoPanorama
{
  public const string CorpoInvalido = nameof(CorpoInvalido);
  public const string ProducaoNaoTerminou = nameof(ProducaoNaoTerminou);
  public const string JaEmbalado = nameof(JaEmbalado);
  public const string NaoEmbalado = nameof(NaoEmbalado);
}

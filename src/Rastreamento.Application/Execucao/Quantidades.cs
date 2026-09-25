using System.Globalization;
using Rastreamento.Application.Estrutura;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// Quantidade que o livro aceita: a faixa de `DECIMAL(18,4)` e NO MAXIMO quatro casas. A terceira
/// condicao nao existia na Fase 2 e importa aqui: o banco arredonda o que passa de quatro casas, e um
/// movimento validado com 0,00005 seria gravado com outro valor — a soma do livro deixaria de bater
/// com o que a validacao aceitou.
/// </summary>
public static class Quantidades
{
  private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

  public static bool CabeNaColuna(decimal quantidade) =>
      quantidade >= PlanejadorDeCopia.QuantidadeMinimaDaColuna
      && quantidade <= PlanejadorDeCopia.QuantidadeMaximaDaColuna
      && decimal.Round(quantidade, 4) == quantidade;

  /// <summary>Para a frase do operador: "2,5", "6", sem zeros a direita.</summary>
  public static string Formatar(decimal quantidade) => quantidade.ToString("0.####", PtBr);
}

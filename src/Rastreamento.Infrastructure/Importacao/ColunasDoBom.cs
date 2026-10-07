using System.Globalization;
using System.Text;
using Rastreamento.Application.Importacao;

namespace Rastreamento.Infrastructure.Importacao;

/// <summary>
/// Localiza as quatro colunas do BOM pelo cabecalho (decisao P9 do plano do import). O cabecalho e
/// normalizado — maiusculas, sem acento, sem pontuacao nem o sinal ordinal de "N°"/"Nº", espacos
/// colapsados — e procurado numa tabela de apelidos. Os apelidos cobrem o SolidWorks em portugues e
/// em ingles; quando o BOM real de outro CAD chegar, a emenda mexe so nesta tabela.
///
/// As quatro colunas tem de estar no cabecalho, o codigo inclusive: ele so pode faltar no
/// CONTEUDO da linha, nunca no cabecalho.
/// </summary>
public static class ColunasDoBom
{
  // Cada entrada: o nome canonico, que o erro cita, e os apelidos ja normalizados.
  private static readonly (string Canonico, string[] Apelidos)[] Colunas =
  [
    ("Nº do item", ["N DO ITEM", "ITEM NO", "ITEM", "NUMERO DO ITEM"]),
    ("Nº da peça", ["N DA PECA", "PART NUMBER", "NUMERO DA PECA"]),
    ("Descrição", ["DESCRICAO", "DESCRIPTION"]),
    ("Quantidade", ["QTD", "QTY", "QUANTIDADE"]),
  ];

  /// <summary>
  /// Os indices das colunas, ou <c>null</c> com um erro por coluna ausente em <paramref name="erros"/>.
  /// Se duas colunas do cabecalho casam com a mesma, vale a primeira.
  /// </summary>
  public static (int Nivel, int Codigo, int Descricao, int Quantidade)? Localizar(
      IReadOnlyList<string> cabecalho, out IReadOnlyList<ErroDoBom> erros)
  {
    var normalizado = cabecalho.Select(Normalizar).ToArray();
    var indices = new int[Colunas.Length];
    var faltando = new List<ErroDoBom>();

    for (var c = 0; c < Colunas.Length; c++)
    {
      indices[c] = Array.FindIndex(normalizado, n => Colunas[c].Apelidos.Contains(n));
      if (indices[c] < 0)
        faltando.Add(new ErroDoBom(null, $"coluna '{Colunas[c].Canonico}' não encontrada"));
    }

    erros = faltando;
    return faltando.Count > 0 ? null : (indices[0], indices[1], indices[2], indices[3]);
  }

  internal static string Normalizar(string texto)
  {
    var decomposto = texto.Normalize(NormalizationForm.FormD);
    var saida = new StringBuilder(decomposto.Length);
    foreach (var c in decomposto)
    {
      var categoria = CharUnicodeInfo.GetUnicodeCategory(c);
      if (categoria == UnicodeCategory.NonSpacingMark) continue;   // o acento solto
      if (c is 'º' or '°' or 'ª') continue;                        // "Nº" vira "N", nao "NO"
      saida.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
    }
    return string.Join(' ', saida.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
  }
}

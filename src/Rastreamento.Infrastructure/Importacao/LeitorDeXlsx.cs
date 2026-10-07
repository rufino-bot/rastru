using System.Globalization;
using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Rastreamento.Application.Importacao;

namespace Rastreamento.Infrastructure.Importacao;

/// <summary>
/// Le o BOM em XLSX (decisao P12 do plano do import): so a PRIMEIRA planilha, e a linha de cabecalho e
/// a primeira com conteudo. Texto compartilhado e inline sao resolvidos; a celula numerica vira o
/// numero, e a de quantidade e formatada sem notacao cientifica (<c>1E-05</c> nao e quantidade valida
/// para quem vem depois).
/// </summary>
internal static class LeitorDeXlsx
{
  // O arquivo e limitado a 5 MiB COMPACTADO; o descompactado de uma bomba de zip e outra conta. A
  // guarda e HEURISTICA: soma o tamanho que cada entrada do zip DECLARA, que um arquivo forjado pode
  // mentir, e a planilha e lida inteira em memoria depois dela.
  private const long TamanhoMaximoDescompactadoEmBytes = 64L * 1024 * 1024;

  // A ultima coluna do Excel, XFD, e a de indice 16383 (a 16384a).
  private const int UltimaColuna = 16383;

  public static ResultadoDaLeituraDoBom Ler(byte[] conteudo)
  {
    try
    {
      if (DescompactadoPassaDoLimite(conteudo))
        return LeitorDeBom.Recusado("o arquivo XLSX descompactado e grande demais.");

      using var memoria = new MemoryStream(conteudo, writable: false);
      using var documento = SpreadsheetDocument.Open(memoria, isEditable: false);
      return LerPrimeiraPlanilha(documento);
    }
    catch (Exception e) when (e is not OutOfMemoryException)
    {
      // Zip invalido, XML malformado, parte ausente: tudo vira "nao consegui ler", sem vazar o detalhe.
      return LeitorDeBom.Recusado("o arquivo XLSX esta corrompido ou nao e uma planilha valida.");
    }
  }

  private static bool DescompactadoPassaDoLimite(byte[] conteudo)
  {
    using var memoria = new MemoryStream(conteudo, writable: false);
    using var zip = new ZipArchive(memoria, ZipArchiveMode.Read);
    return zip.Entries.Sum(e => e.Length) > TamanhoMaximoDescompactadoEmBytes;
  }

  private static ResultadoDaLeituraDoBom LerPrimeiraPlanilha(SpreadsheetDocument documento)
  {
    var livro = documento.WorkbookPart ?? throw new InvalidDataException("sem livro");
    var primeira = livro.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault()
        ?? throw new InvalidDataException("sem planilha");
    var planilha = (WorksheetPart)livro.GetPartById(primeira.Id!.Value!);
    var textos = livro.SharedStringTablePart?.SharedStringTable?
        .Elements<SharedStringItem>().Select(t => t.InnerText).ToList() ?? [];

    var linhas = new List<(int Numero, IReadOnlyList<Celula> Celulas)>();
    var proximaLinha = 1;
    foreach (var linha in (planilha.Worksheet ?? throw new InvalidDataException("planilha sem conteudo")).Descendants<Row>())
    {
      var numero = linha.RowIndex?.Value is { } indice ? (int)indice : proximaLinha;
      proximaLinha = numero + 1;

      // So as celulas que existem, pela coluna: preencher as vazias ate uma celula na coluna XFD custaria
      // 16 mil celulas por linha, e o arquivo pode ter centenas de milhares de linhas.
      var celulas = new Dictionary<int, Celula>();
      var proximaColuna = 0;
      foreach (var celula in linha.Elements<Cell>())
      {
        var coluna = ColunaDe(celula.CellReference?.Value) ?? proximaColuna;
        if (coluna > UltimaColuna)
          throw new InvalidDataException("coluna alem da XFD");
        celulas[coluna] = Resolver(celula, textos);
        proximaColuna = coluna + 1;
      }
      if (celulas.Values.Any(c => c.Texto.Length > 0)) linhas.Add((numero, new CelulasEsparsas(celulas)));
    }

    if (linhas.Count == 0)
      return LeitorDeBom.Recusado("o arquivo esta vazio: nao ha linha de cabecalho.");

    var cabecalho = linhas[0].Celulas.Select(c => c.Texto).ToList();
    return LeitorDeBom.MontarLinhas(cabecalho, linhas.Skip(1));
  }

  private static Celula Resolver(Cell celula, IReadOnlyList<string> textos)
  {
    var tipo = celula.DataType?.Value;
    if (tipo == CellValues.InlineString)
      return new Celula((celula.InlineString?.InnerText ?? "").Trim(), false);

    var cru = celula.CellValue?.Text ?? "";
    if (tipo == CellValues.SharedString)
    {
      var texto = int.TryParse(cru, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < textos.Count
          ? textos[i]
          : "";
      return new Celula(texto.Trim(), false);
    }

    if (tipo is null || tipo == CellValues.Number)
      return new Celula(cru.Trim(), cru.Trim().Length > 0);

    return new Celula(cru.Trim(), false);   // string de formula, booleano, erro
  }

  /// <summary>
  /// "A" é 0, "B" é 1, "AA" é 26; <c>null</c> sem referencia legivel. Mais de tres letras ja passa da
  /// <see cref="UltimaColuna"/>, e a conta para ali, antes de o numero crescer.
  /// </summary>
  private static int? ColunaDe(string? referencia)
  {
    if (string.IsNullOrEmpty(referencia)) return null;
    var coluna = 0;
    var letras = 0;
    foreach (var c in referencia)
    {
      if (!char.IsAsciiLetter(c)) break;
      if (++letras > 3) return int.MaxValue;
      coluna = coluna * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
    }
    return letras == 0 ? null : coluna - 1;
  }

  /// <summary>
  /// Uma linha da planilha como lista: a coluna sem celula e <see cref="Celula.Vazia"/>, sem ocupar
  /// memoria. <c>Count</c> vai ate a ultima coluna com celula.
  /// </summary>
  private sealed class CelulasEsparsas(Dictionary<int, Celula> celulas) : IReadOnlyList<Celula>
  {
    public int Count { get; } = celulas.Count == 0 ? 0 : celulas.Keys.Max() + 1;

    public Celula this[int indice] => celulas.TryGetValue(indice, out var c) ? c : Celula.Vazia;

    public IEnumerator<Celula> GetEnumerator()
    {
      for (var i = 0; i < Count; i++) yield return this[i];
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
  }
}

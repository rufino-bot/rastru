using System.Text;
using Rastreamento.Application.Importacao;

namespace Rastreamento.Infrastructure.Importacao;

/// <summary>
/// Le o BOM em CSV (decisao P12 do plano do import). Separador <c>;</c> ou <c>,</c>, decidido pela linha
/// de cabecalho; aspas no padrao RFC 4180 (campo entre aspas pode ter separador, quebra de linha e
/// <c>""</c> para uma aspa). Parser proprio, de dois estados — fora e dentro de aspas — porque nao vale
/// uma biblioteca para isso.
///
/// Codificacao: UTF-8 com BOM; sem BOM, UTF-8 se o arquivo for UTF-8 valido, senao Windows-1252, que e o
/// que o Excel brasileiro grava. Ler UTF-8 sem BOM como Windows-1252 estragaria o acento em silencio.
/// </summary>
internal static class LeitorDeCsv
{
  private static readonly byte[] PreambuloUtf8 = [0xEF, 0xBB, 0xBF];

  static LeitorDeCsv() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

  public static ResultadoDaLeituraDoBom Ler(byte[] conteudo)
  {
    var texto = Decodificar(conteudo);
    var registros = Separar(texto, DetectarSeparador(texto), out var erroDeAspas);
    if (erroDeAspas is not null) return LeitorDeBom.Recusado([erroDeAspas]);

    var comConteudo = registros.Where(r => r.Campos.Any(c => c.Length > 0)).ToList();
    if (comConteudo.Count == 0)
      return LeitorDeBom.Recusado("o arquivo está vazio: não há linha de cabeçalho.");

    var cabecalho = comConteudo[0].Campos;
    var linhas = comConteudo.Skip(1)
        .Select(r => (r.Linha, (IReadOnlyList<Celula>)r.Campos.Select(c => new Celula(c, false)).ToList()));
    return LeitorDeBom.MontarLinhas(cabecalho, linhas);
  }

  private static string Decodificar(byte[] conteudo)
  {
    if (conteudo.AsSpan().StartsWith(PreambuloUtf8))
      return new UTF8Encoding(false).GetString(conteudo, PreambuloUtf8.Length, conteudo.Length - PreambuloUtf8.Length);

    try
    {
      return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(conteudo);
    }
    catch (DecoderFallbackException)
    {
      return Encoding.GetEncoding(1252).GetString(conteudo);
    }
  }

  /// <summary>Conta os dois separadores na primeira linha fisica fora de aspas; empate fica com o ponto e virgula do Excel pt-BR.</summary>
  private static char DetectarSeparador(string texto)
  {
    int pontoEVirgula = 0, virgula = 0;
    var dentroDeAspas = false;
    foreach (var c in texto)
    {
      if (c == '"') dentroDeAspas = !dentroDeAspas;
      else if (!dentroDeAspas && (c == '\n' || c == '\r'))
      {
        if (pontoEVirgula + virgula > 0) break;   // pula linhas em branco antes do cabecalho
      }
      else if (!dentroDeAspas && c == ';') pontoEVirgula++;
      else if (!dentroDeAspas && c == ',') virgula++;
    }
    return virgula > pontoEVirgula ? ',' : ';';
  }

  /// <summary>
  /// Quebra o texto em registros, cada um com a linha FISICA em que comeca (a que o operador ve no
  /// editor, mesmo havendo campo com quebra de linha). Os campos saem aparados.
  /// </summary>
  private static List<(int Linha, List<string> Campos)> Separar(string texto, char separador, out ErroDoBom? erro)
  {
    var registros = new List<(int, List<string>)>();
    var campos = new List<string>();
    var campo = new StringBuilder();
    var linhaAtual = 1;
    var linhaDoRegistro = 1;
    var dentroDeAspas = false;
    var campoComAspas = false;

    void FecharCampo()
    {
      campos.Add(campo.ToString().Trim());
      campo.Clear();
      campoComAspas = false;
    }

    void FecharRegistro()
    {
      FecharCampo();
      registros.Add((linhaDoRegistro, campos));
      campos = [];
    }

    for (var i = 0; i < texto.Length; i++)
    {
      var c = texto[i];
      if (dentroDeAspas)
      {
        if (c == '"')
        {
          if (i + 1 < texto.Length && texto[i + 1] == '"') { campo.Append('"'); i++; }
          else dentroDeAspas = false;
        }
        else if (c == '\r')
        {
          if (i + 1 < texto.Length && texto[i + 1] == '\n') i++;
          campo.Append('\n');
          linhaAtual++;
        }
        else
        {
          if (c == '\n') linhaAtual++;
          campo.Append(c);
        }
      }
      else if (c == '"' && campo.Length == 0 && !campoComAspas)
      {
        dentroDeAspas = true;
        campoComAspas = true;
      }
      else if (c == separador) FecharCampo();
      else if (c == '\r' || c == '\n')
      {
        if (c == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n') i++;
        FecharRegistro();
        linhaAtual++;
        linhaDoRegistro = linhaAtual;
      }
      else campo.Append(c);
    }

    if (dentroDeAspas)
    {
      erro = new ErroDoBom(linhaDoRegistro, "aspas não fechadas: um campo entre aspas não termina.");
      return [];
    }

    if (campo.Length > 0 || campos.Count > 0) FecharRegistro();
    erro = null;
    return registros;
  }
}

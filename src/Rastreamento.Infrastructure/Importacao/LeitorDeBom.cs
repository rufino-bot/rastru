using System.Globalization;
using Rastreamento.Application.Importacao;

namespace Rastreamento.Infrastructure.Importacao;

/// <summary>Despacha o arquivo do BOM para o leitor do formato, pela extensao (sem distinguir caixa).</summary>
public sealed class LeitorDeBom : ILeitorDeBom
{
  /// <summary>Limite do arquivo (decisao P11 do plano do import).</summary>
  public const int TamanhoMaximoEmBytes = 5 * 1024 * 1024;

  public ResultadoDaLeituraDoBom Ler(string nomeDoArquivo, byte[] conteudo)
  {
    if (conteudo.Length > TamanhoMaximoEmBytes)
      return Recusado($"o arquivo passa de {TamanhoMaximoEmBytes / (1024 * 1024)} MiB.");

    var extensao = Path.GetExtension(nomeDoArquivo);
    if (extensao.Equals(".csv", StringComparison.OrdinalIgnoreCase)) return LeitorDeCsv.Ler(conteudo);
    if (extensao.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) return LeitorDeXlsx.Ler(conteudo);

    return Recusado("extensão não suportada: envie um arquivo .csv ou .xlsx.");
  }

  /// <summary>
  /// A celula numerica do XLSX guarda o <c>double</c> do Excel em notacao de armazenamento: <c>1E-05</c>,
  /// <c>0.30000000000000004</c>. Formatada com 12 casas e sem expoente, vira <c>0.00001</c> e <c>0.3</c> —
  /// o ruido do ponto flutuante fica muito alem das 4 casas que a quantidade aceita. O texto de CSV
  /// segue como o operador o escreveu.
  /// </summary>
  private static string QuantidadeEmTexto(Celula quantidade) =>
      quantidade.Numerica
      && decimal.TryParse(quantidade.Texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var numero)
          ? numero.ToString("0.############", CultureInfo.InvariantCulture)
          : quantidade.Texto;

  internal static ResultadoDaLeituraDoBom Recusado(string mensagem) =>
      new([], [new ErroDoBom(null, mensagem)]);

  internal static ResultadoDaLeituraDoBom Recusado(IReadOnlyList<ErroDoBom> erros) => new([], erros);

  /// <summary>
  /// O que os dois leitores fazem depois de ter uma tabela de celulas: localizar as colunas pelo
  /// cabecalho e montar as linhas cruas, pulando as que nao tem nada nas quatro colunas lidas.
  /// </summary>
  internal static ResultadoDaLeituraDoBom MontarLinhas(
      IReadOnlyList<string> cabecalho, IEnumerable<(int Numero, IReadOnlyList<Celula> Celulas)> linhas)
  {
    if (ColunasDoBom.Localizar(cabecalho, out var ausentes) is not var (nivel, codigo, descricao, quantidade))
      return Recusado(ausentes);

    var lidas = new List<LinhaCruaDoBom>();
    var erros = new List<ErroDoBom>();
    foreach (var (numero, celulas) in linhas)
    {
      Celula Em(int i) => i < celulas.Count ? celulas[i] : Celula.Vazia;
      var (n, c, d, q) = (Em(nivel), Em(codigo), Em(descricao), Em(quantidade));
      if (n.Texto.Length == 0 && c.Texto.Length == 0 && d.Texto.Length == 0 && q.Texto.Length == 0) continue;

      // "1.10" e "1.1" sao a mesma celula numerica: o nivel so e confiavel como texto. O inteiro nao
      // tem essa ambiguidade ("2", "10"), entao passa.
      if (n.Numerica && !n.Texto.All(char.IsAsciiDigit))
      {
        erros.Add(new ErroDoBom(numero,
            "o nº do item veio como número; salve a coluna como texto (1.10 e 1.1 seriam indistinguíveis)."));
        continue;
      }

      lidas.Add(new LinhaCruaDoBom(numero, n.Texto, c.Texto.Length == 0 ? null : c.Texto, d.Texto, QuantidadeEmTexto(q)));
    }

    return erros.Count > 0 ? Recusado(erros) : new ResultadoDaLeituraDoBom(lidas, []);
  }
}

/// <summary>Uma celula ja em texto, aparada. <c>Numerica</c> so e verdadeira para a celula numerica do XLSX.</summary>
internal readonly record struct Celula(string Texto, bool Numerica)
{
  public static readonly Celula Vazia = new("", false);
}

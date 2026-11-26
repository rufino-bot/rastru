using System.Text.RegularExpressions;

namespace Rastreamento.Infrastructure.Banco;

/// <summary>
/// Separa um script T-SQL nos lotes que o <c>sqlcmd</c> separaria: <c>GO</c> não é T-SQL, é comando do
/// cliente, e o <c>SqlCommand</c> não o entende.
/// </summary>
public static class LotesDeSql
{
  private static readonly Regex LinhaDeGo =
      new(@"^[ \t]*GO(?:[ \t]+(?<resto>\S.*?))?[ \t]*\r?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

  /// <summary>
  /// Separa nas linhas que são só <c>GO</c> (sem diferenciar caixa, espaços em volta); lote vazio some.
  /// <c>GO &lt;n&gt;</c> lança <see cref="FormatException"/>.
  /// </summary>
  public static IReadOnlyList<string> Separar(string script)
  {
    var lotes = new List<string>();
    var atual = new List<string>();

    void Fechar()
    {
      var texto = string.Join("\n", atual);
      if (!string.IsNullOrWhiteSpace(texto)) lotes.Add(texto);
      atual.Clear();
    }

    foreach (var linha in script.Split('\n'))
    {
      var m = LinhaDeGo.Match(linha);
      if (m.Success)
      {
        var resto = m.Groups["resto"].Value;
        if (resto.Length > 0 && !resto.StartsWith("--", StringComparison.Ordinal))
        {
          if (int.TryParse(resto, out _))
            throw new FormatException("GO com contagem não é suportado.");
          throw new FormatException($"Linha começando por GO não reconhecida: \"{linha.Trim()}\".");
        }
        Fechar();
      }
      else
      {
        atual.Add(linha.TrimEnd('\r'));
      }
    }
    Fechar();
    return lotes;
  }
}

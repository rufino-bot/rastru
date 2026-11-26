using System.Text.RegularExpressions;

namespace Rastreamento.Infrastructure.Banco;

/// <summary>
/// Onde estão os scripts SQL. A raiz é o repositório nos testes e <c>/app/sql</c> na imagem, com o mesmo layout.
/// </summary>
public sealed record ScriptsDoBanco(string Raiz)
{
  private static readonly Regex NomeValido = new(@"^\d{4}-[a-z0-9-]+\.sql$", RegexOptions.Compiled);

  public string Modelo => Path.Combine(Raiz, "specs", "02-modelo-de-dados.sql");
  public string Seed => Path.Combine(Raiz, "db", "seed.sql");
  public string SeedDemo => Path.Combine(Raiz, "db", "seed-demo.sql");
  public string MassaVitrine => Path.Combine(Raiz, "db", "massa-vitrine.sql");
  public string PastaDeMigracoes => Path.Combine(Raiz, "db", "migracoes");

  /// <summary>Nomes de arquivo das migrações, em ordem ordinal. Arquivo <c>.sql</c> fora do padrão lança.</summary>
  public IReadOnlyList<string> Migracoes()
  {
    // Pasta ausente é raiz errada ou COPY quebrado, nunca "nada pendente": falha alta, antes de qualquer backup.
    if (!Directory.Exists(PastaDeMigracoes))
      throw new DirectoryNotFoundException($"Pasta de migrações não encontrada: {PastaDeMigracoes}.");
    var nomes = Directory.EnumerateFiles(PastaDeMigracoes, "*.sql")
        .Select(Path.GetFileName)
        .OfType<string>()
        .ToList();
    var invalido = nomes.FirstOrDefault(n => !NomeValido.IsMatch(n));
    if (invalido is not null)
      throw new InvalidOperationException(
          $"Arquivo de migração fora do padrão NNNN-descricao.sql (minúsculas, dígitos e hífens): {invalido}.");
    nomes.Sort(StringComparer.Ordinal);
    return nomes;
  }
}

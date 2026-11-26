using System.Text;
using Microsoft.Data.SqlClient;

namespace Rastreamento.Infrastructure.Banco;

public sealed record ResultadoDaMigracao(IReadOnlyList<string> Aplicadas, string? Backup);

/// <summary>
/// Aplica as migrações de <c>db/migracoes/</c> que ainda não estão em <c>dbo.MigracaoAplicada</c>, em ordem de
/// nome. Um backup antes da primeira pendente; cada pendente numa transação própria com o <c>INSERT</c> do nome.
/// Migração que não roda em transação não é suportada, e a falha é alta: a primeira que falhar desfaz a própria
/// transação e propaga a exceção, e as seguintes não rodam.
/// </summary>
public sealed class MigradorDeBanco(IBackupDoBanco backup)
{
  public async Task<ResultadoDaMigracao> MigrarAsync(
      string conexaoAdministrativa, string banco, ScriptsDoBanco scripts, CancellationToken ct)
  {
    var todas = scripts.Migracoes();

    var conexaoDoBanco = new SqlConnectionStringBuilder(conexaoAdministrativa) { InitialCatalog = banco }.ConnectionString;
    await using var conexao = new SqlConnection(conexaoDoBanco);
    await conexao.OpenAsync(ct);

    var jaAplicadas = await LerAplicadasAsync(conexao, ct);
    var pendentes = todas.Where(n => !jaAplicadas.Contains(n)).ToList();
    if (pendentes.Count == 0) return new ResultadoDaMigracao([], null);

    var caminhoDoBackup = await backup.FazerAsync(conexaoAdministrativa, banco, ct);

    var aplicadas = new List<string>();
    foreach (var nome in pendentes)
    {
      var script = await File.ReadAllTextAsync(Path.Combine(scripts.PastaDeMigracoes, nome), Encoding.UTF8, ct);
      await using var transacao = (SqlTransaction)await conexao.BeginTransactionAsync(ct);
      try
      {
        await ExecutorDeSql.ExecutarAsync(conexao, transacao, script, ct);
        await using var anotar = new SqlCommand(
            "INSERT INTO dbo.MigracaoAplicada (Nome) VALUES (@nome)", conexao, transacao);
        anotar.Parameters.AddWithValue("@nome", nome);
        await anotar.ExecuteNonQueryAsync(ct);
        await transacao.CommitAsync(ct);
      }
      catch
      {
        try { await transacao.RollbackAsync(CancellationToken.None); }
        catch (InvalidOperationException) { /* a transação já foi encerrada pelo servidor */ }
        throw;
      }
      aplicadas.Add(nome);
    }
    return new ResultadoDaMigracao(aplicadas, caminhoDoBackup);
  }

  private static async Task<HashSet<string>> LerAplicadasAsync(SqlConnection conexao, CancellationToken ct)
  {
    var nomes = new HashSet<string>(StringComparer.Ordinal);
    await using var existe = new SqlCommand("SELECT OBJECT_ID('dbo.MigracaoAplicada')", conexao);
    if (await existe.ExecuteScalarAsync(ct) is null or DBNull) return nomes;

    await using var cmd = new SqlCommand("SELECT Nome FROM dbo.MigracaoAplicada", conexao);
    await using var leitor = await cmd.ExecuteReaderAsync(ct);
    while (await leitor.ReadAsync(ct)) nomes.Add(leitor.GetString(0));
    return nomes;
  }
}

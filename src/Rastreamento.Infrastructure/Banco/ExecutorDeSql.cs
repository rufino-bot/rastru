using Microsoft.Data.SqlClient;

namespace Rastreamento.Infrastructure.Banco;

/// <summary>
/// Executa um script com <c>GO</c> lote a lote. Quem o chama lê o arquivo como UTF-8 (o
/// <c>-f 65001</c> deste caminho).
/// </summary>
public static class ExecutorDeSql
{
  private const int TempoLimiteEmSegundos = 600;

  public static async Task ExecutarAsync(
      SqlConnection conexao, SqlTransaction? transacao, string script, CancellationToken ct)
  {
    foreach (var lote in LotesDeSql.Separar(script))
    {
      await using var comando = new SqlCommand(lote, conexao, transacao) { CommandTimeout = TempoLimiteEmSegundos };
      await comando.ExecuteNonQueryAsync(ct);
    }
  }
}

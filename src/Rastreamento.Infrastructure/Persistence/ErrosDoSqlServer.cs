using Microsoft.Data.SqlClient;

namespace Rastreamento.Infrastructure.Persistence;

/// <summary>
/// Os numeros de erro do SQL Server que os repositorios traduzem. Extraido de
/// `ReceitaPadraoRepository.EhConflitoDeConcorrencia` quando o segundo consumidor chegou
/// (`ExecucaoRepository`, Fase 3): o criterio e o mesmo, e o comentario de la continua valendo.
/// </summary>
internal static class ErrosDoSqlServer
{
  /// <summary>
  /// 1205 = vitima de deadlock; 1222 = lock timeout. Percorre a CADEIA de inner exceptions, porque a
  /// profundidade varia com o caminho (SqlException pelado numa consulta, embrulhado em
  /// DbUpdateException num SaveChanges). Qualquer outro numero passa cru de proposito.
  /// </summary>
  public static bool EhConflitoDeConcorrencia(Exception e)
  {
    for (Exception? atual = e; atual is not null; atual = atual.InnerException)
      if (atual is SqlException sql && sql.Number is 1205 or 1222) return true;

    return false;
  }
}

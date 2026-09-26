using Microsoft.Data.SqlClient;

namespace Rastreamento.Infrastructure.Persistence;

/// <summary>
/// Os numeros de erro do SQL Server que os repositorios traduzem. Extraido de
/// `ReceitaPadraoRepository.EhConflitoDeConcorrencia` quando o segundo consumidor chegou
/// (`ExecucaoRepository`, Fase 3): o criterio e o mesmo, e o comentario de la continua valendo.
///
/// <para>
/// `EhDeadlock`/`EhLockTimeout` separam os dois numeros que `EhConflitoDeConcorrencia` juntava — a
/// review da Task 11 (retry de deadlock em `ExecucaoRepository.EmTransacaoAsync`) precisa saber QUAL
/// dos dois aconteceu: 1205 tenta de novo, 1222 nao. `EhConflitoDeConcorrencia` continua valendo para
/// quem so precisa saber "foi um dos dois" (o catch final que vira 409).
/// </para>
/// </summary>
internal static class ErrosDoSqlServer
{
  /// <summary>1205 = escolhido pelo SQL Server como vitima de um deadlock (ciclo de esperas).</summary>
  public static bool EhDeadlock(Exception e) => TemNumero(e, 1205);

  /// <summary>1222 = lock timeout: a espera por uma trava passou de `LOCK_TIMEOUT` sem ciclo nenhum.</summary>
  public static bool EhLockTimeout(Exception e) => TemNumero(e, 1222);

  public static bool EhConflitoDeConcorrencia(Exception e) => EhDeadlock(e) || EhLockTimeout(e);

  /// <summary>
  /// Percorre a CADEIA de inner exceptions, porque a profundidade varia com o caminho (SqlException
  /// pelado numa consulta, embrulhado em DbUpdateException num SaveChanges). Qualquer outro numero
  /// passa cru de proposito.
  /// </summary>
  private static bool TemNumero(Exception e, int numero)
  {
    for (Exception? atual = e; atual is not null; atual = atual.InnerException)
      if (atual is SqlException sql && sql.Number == numero) return true;

    return false;
  }
}

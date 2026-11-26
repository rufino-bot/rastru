using Microsoft.Data.SqlClient;

namespace Rastreamento.Infrastructure.Banco;

public interface IBackupDoBanco
{
  /// <summary>Faz o backup completo do banco e devolve o caminho do arquivo gravado (no servidor).</summary>
  Task<string> FazerAsync(string conexaoAdministrativa, string banco, CancellationToken ct);
}

/// <summary>
/// <c>BACKUP DATABASE</c> na pasta padrão de backup da instância, em um de três arquivos
/// (<c>{banco}-1.bak</c> a <c>-3.bak</c>): o que nunca foi usado ou, se todos já foram, o de backup mais antigo
/// segundo o <c>msdb</c>. Sem <c>COMPRESSION</c>: o SQL Server Express não tem.
/// </summary>
public sealed class BackupDoBanco : IBackupDoBanco
{
  private const int Copias = 3;
  private const int TempoLimiteEmSegundos = 3600;

  public async Task<string> FazerAsync(string conexaoAdministrativa, string banco, CancellationToken ct)
  {
    await using var conexao = new SqlConnection(conexaoAdministrativa);
    await conexao.OpenAsync(ct);

    var pasta = await PastaDeBackupAsync(conexao, ct);
    var separador = pasta.Contains('\\') ? '\\' : '/';
    var caminhos = Enumerable.Range(1, Copias)
        .Select(i => $"{pasta.TrimEnd('\\', '/')}{separador}{banco}-{i}.bak")
        .ToList();

    var ultimo = new Dictionary<string, int?>();
    foreach (var caminho in caminhos)
      ultimo[caminho] = await UltimoBackupAsync(conexao, caminho, ct);

    // Nunca usado vem primeiro; entre os usados, o mais antigo. A ordem é a do backup_set_id (crescente no msdb),
    // e não a de backup_finish_date, que tem resolução de segundo e empata em backups seguidos.
    var escolhido = caminhos
        .OrderBy(c => ultimo[c].HasValue)
        .ThenBy(c => ultimo[c] ?? 0)
        .First();

    var nomeLogico = "[" + banco.Replace("]", "]]") + "]";
    await using var cmd = new SqlCommand(
        $"BACKUP DATABASE {nomeLogico} TO DISK = @caminho WITH INIT, FORMAT", conexao)
    { CommandTimeout = TempoLimiteEmSegundos };
    cmd.Parameters.AddWithValue("@caminho", escolhido);
    await cmd.ExecuteNonQueryAsync(ct);
    return escolhido;
  }

  private static async Task<string> PastaDeBackupAsync(SqlConnection conexao, CancellationToken ct)
  {
    await using var cmd = new SqlCommand("SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS NVARCHAR(512))", conexao);
    return await cmd.ExecuteScalarAsync(ct) as string
        ?? throw new InvalidOperationException("A instância não informou a pasta padrão de backup.");
  }

  private static async Task<int?> UltimoBackupAsync(SqlConnection conexao, string caminho, CancellationToken ct)
  {
    await using var cmd = new SqlCommand(
        "SELECT MAX(s.backup_set_id) FROM msdb.dbo.backupset s " +
        "JOIN msdb.dbo.backupmediafamily f ON f.media_set_id = s.media_set_id " +
        "WHERE f.physical_device_name = @caminho", conexao);
    cmd.Parameters.AddWithValue("@caminho", caminho);
    var valor = await cmd.ExecuteScalarAsync(ct);
    return valor is int n ? n : null;
  }
}

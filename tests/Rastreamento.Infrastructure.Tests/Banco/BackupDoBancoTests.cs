using Microsoft.Data.SqlClient;
using Rastreamento.Infrastructure.Banco;

namespace Rastreamento.Infrastructure.Tests.Banco;

/// <summary>
/// Banco de nome fixo, recriado a cada teste, só para os arquivos <c>.bak</c> não se acumularem entre
/// execuções (o nome do arquivo vem do nome do banco).
/// </summary>
public class BackupDoBancoTests
{
  private const string NomeFixo = "RastruTesteDoBackup";

  [Fact]
  public async Task Faz_o_backup_e_devolve_o_caminho()
  {
    await using var banco = await BancoDescartavel.CriarAsync(NomeFixo);

    var caminho = await new BackupDoBanco().FazerAsync(banco.ConexaoAdministrativa, banco.Nome, CancellationToken.None);

    Assert.EndsWith(".bak", caminho);
    await using var conexao = new SqlConnection(banco.ConexaoAdministrativa);
    await conexao.OpenAsync();
    await using var cmd = new SqlCommand(
        "SELECT TOP 1 s.backup_size FROM msdb.dbo.backupset s " +
        "JOIN msdb.dbo.backupmediafamily f ON f.media_set_id = s.media_set_id " +
        "WHERE f.physical_device_name = @c ORDER BY s.backup_finish_date DESC", conexao);
    cmd.Parameters.AddWithValue("@c", caminho);
    var tamanho = Convert.ToInt64(await cmd.ExecuteScalarAsync());
    Assert.True(tamanho > 0);
  }

  [Fact]
  public async Task Quarto_backup_reusa_o_slot_mais_antigo()
  {
    await using var banco = await BancoDescartavel.CriarAsync(NomeFixo);
    var backup = new BackupDoBanco();

    var caminhos = new List<string>();
    for (var i = 0; i < 4; i++)
      caminhos.Add(await backup.FazerAsync(banco.ConexaoAdministrativa, banco.Nome, CancellationToken.None));

    Assert.Equal(3, caminhos.Take(3).Distinct().Count());
    Assert.Equal(caminhos[0], caminhos[3]);
  }
}

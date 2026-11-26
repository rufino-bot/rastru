using Microsoft.Data.SqlClient;
using Rastreamento.Infrastructure.Banco;

namespace Rastreamento.Infrastructure.Tests.Banco;

public class MigradorDeBancoTests : IDisposable
{
  private readonly string _raiz = Path.Combine(Path.GetTempPath(), "rastru-migracoes-" + Guid.NewGuid().ToString("N"));
  private readonly string _pasta;

  public MigradorDeBancoTests()
  {
    _pasta = Path.Combine(_raiz, "db", "migracoes");
    Directory.CreateDirectory(_pasta);
  }

  public void Dispose()
  {
    if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true);
  }

  [Fact]
  public async Task Aplica_em_ordem_de_nome_e_anota()
  {
    await using var banco = await BancoDescartavel.CriarAsync();
    Escrever("0002-b.sql", "CREATE TABLE dbo.B (Id INT);");
    CopiarA0001();
    Escrever("0010-c.sql", "CREATE TABLE dbo.C (Id INT);");

    var resultado = await Migrar(banco, new BackupFalso(banco));

    Assert.Equal(["0001-migracao-aplicada.sql", "0002-b.sql", "0010-c.sql"], resultado.Aplicadas);
    Assert.Equal(["0001-migracao-aplicada.sql", "0002-b.sql", "0010-c.sql"], await Anotadas(banco));
  }

  [Fact]
  public async Task Pula_as_ja_aplicadas()
  {
    await using var banco = await BancoDescartavel.CriarAsync();
    CopiarA0001();
    Escrever("0002-b.sql", "CREATE TABLE dbo.B (Id INT);");
    await Migrar(banco, new BackupFalso(banco));
    var segundo = new BackupFalso(banco);

    var resultado = await Migrar(banco, segundo);

    Assert.Empty(resultado.Aplicadas);
    Assert.Null(resultado.Backup);
    Assert.Equal(0, segundo.Chamadas);
  }

  [Fact]
  public async Task Backup_vem_antes_da_primeira_pendente()
  {
    await using var banco = await BancoDescartavel.CriarAsync();
    CopiarA0001();
    var falso = new BackupFalso(banco);

    var resultado = await Migrar(banco, falso);

    Assert.Equal(1, falso.Chamadas);
    // No instante do backup a 0001 ainda não tinha rodado: a tabela não existia.
    Assert.False(falso.TabelaExistiaNoBackup);
    Assert.Equal(falso.Caminho, resultado.Backup);
    Assert.Single(resultado.Aplicadas);
  }

  [Fact]
  public async Task Script_que_falha_nao_anota_e_desfaz()
  {
    await using var banco = await BancoDescartavel.CriarAsync();
    CopiarA0001();
    Escrever("0002-falha.sql", "CREATE TABLE dbo.Parcial (Id INT);\nGO\nSELECT 1/0;");
    Escrever("0003-depois.sql", "CREATE TABLE dbo.Depois (Id INT);");
    var falso = new BackupFalso(banco);

    await Assert.ThrowsAnyAsync<SqlException>(() => Migrar(banco, falso));

    Assert.Equal(1, falso.Chamadas);
    Assert.Equal(["0001-migracao-aplicada.sql"], await Anotadas(banco));
    Assert.False(await Existe(banco, "dbo.Parcial"));
    Assert.False(await Existe(banco, "dbo.Depois"));
  }

  [Fact]
  public async Task Arquivo_fora_do_padrao_lanca()
  {
    await using var banco = await BancoDescartavel.CriarAsync();
    Escrever("migracao.sql", "SELECT 1;");

    await Assert.ThrowsAsync<InvalidOperationException>(() => Migrar(banco, new BackupFalso(banco)));
  }

  [Fact]
  public void Pasta_de_migracoes_ausente_lanca()
  {
    var scripts = new ScriptsDoBanco(Path.Combine(_raiz, "inexistente"));

    var erro = Assert.Throws<DirectoryNotFoundException>(() => scripts.Migracoes());

    Assert.Contains("migracoes", erro.Message);
  }

  [Fact]
  public async Task O_modelo_tem_a_tabela_de_migracoes()
  {
    await using var banco = await BancoDescartavel.CriarAsync();
    var modelo = new ScriptsDoBanco(RaizDoRepositorio()).Modelo;

    await using (var conexao = new SqlConnection(banco.ConexaoDoBanco))
    {
      await conexao.OpenAsync();
      await ExecutorDeSql.ExecutarAsync(
          conexao, null, await File.ReadAllTextAsync(modelo, System.Text.Encoding.UTF8), CancellationToken.None);
    }

    await using var c = new SqlConnection(banco.ConexaoDoBanco);
    await c.OpenAsync();
    await using var cmd = new SqlCommand(
        "SELECT " +
        "(SELECT COUNT(*) FROM sys.key_constraints k JOIN sys.index_columns ic ON ic.object_id = k.parent_object_id " +
        "  AND ic.index_id = k.unique_index_id JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id " +
        "  WHERE k.type = 'PK' AND k.parent_object_id = OBJECT_ID('dbo.MigracaoAplicada') AND col.name = 'Nome'), " +
        "(SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MigracaoAplicada') AND name = 'Nome'), " +
        "(SELECT t.name FROM sys.columns col JOIN sys.types t ON t.user_type_id = col.user_type_id " +
        "  WHERE col.object_id = OBJECT_ID('dbo.MigracaoAplicada') AND col.name = 'Nome'), " +
        "(SELECT COUNT(*) FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('dbo.MigracaoAplicada') " +
        "  AND parent_column_id = (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MigracaoAplicada') AND name = 'AplicadaEm'))", c);
    await using var r = await cmd.ExecuteReaderAsync();
    Assert.True(await r.ReadAsync());
    Assert.Equal(1, r.GetInt32(0));
    Assert.Equal(400, Convert.ToInt32(r.GetValue(1))); // NVARCHAR(200) = 400 bytes
    Assert.Equal("nvarchar", r.GetString(2));
    Assert.Equal(1, r.GetInt32(3));
  }

  private Task<ResultadoDaMigracao> Migrar(BancoDescartavel banco, IBackupDoBanco backup) =>
      new MigradorDeBanco(backup).MigrarAsync(
          banco.ConexaoAdministrativa, banco.Nome, new ScriptsDoBanco(_raiz), CancellationToken.None);

  private void Escrever(string nome, string conteudo) => File.WriteAllText(Path.Combine(_pasta, nome), conteudo);

  private void CopiarA0001() =>
      File.Copy(
          Path.Combine(RaizDoRepositorio(), "db", "migracoes", "0001-migracao-aplicada.sql"),
          Path.Combine(_pasta, "0001-migracao-aplicada.sql"));

  private static async Task<List<string>> Anotadas(BancoDescartavel banco)
  {
    await using var c = new SqlConnection(banco.ConexaoDoBanco);
    await c.OpenAsync();
    await using var cmd = new SqlCommand("SELECT Nome FROM dbo.MigracaoAplicada ORDER BY Nome", c);
    await using var r = await cmd.ExecuteReaderAsync();
    var nomes = new List<string>();
    while (await r.ReadAsync()) nomes.Add(r.GetString(0));
    return nomes;
  }

  private static async Task<bool> Existe(BancoDescartavel banco, string tabela)
  {
    await using var c = new SqlConnection(banco.ConexaoDoBanco);
    await c.OpenAsync();
    await using var cmd = new SqlCommand("SELECT OBJECT_ID(@t)", c);
    cmd.Parameters.AddWithValue("@t", tabela);
    return await cmd.ExecuteScalarAsync() is not (null or DBNull);
  }

  private static string RaizDoRepositorio()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rastreamento.slnx")))
      dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
  }

  /// <summary>Não faz backup: anota a chamada e olha se a tabela de migrações já existia naquele instante.</summary>
  private sealed class BackupFalso(BancoDescartavel banco) : IBackupDoBanco
  {
    public int Chamadas { get; private set; }
    public bool TabelaExistiaNoBackup { get; private set; }
    public string Caminho => "/falso/backup.bak";

    public async Task<string> FazerAsync(string conexaoAdministrativa, string nomeDoBanco, CancellationToken ct)
    {
      Chamadas++;
      await using var c = new SqlConnection(banco.ConexaoDoBanco);
      await c.OpenAsync(ct);
      await using var cmd = new SqlCommand("SELECT OBJECT_ID('dbo.MigracaoAplicada')", c);
      TabelaExistiaNoBackup = await cmd.ExecuteScalarAsync(ct) is not (null or DBNull);
      return Caminho;
    }
  }
}

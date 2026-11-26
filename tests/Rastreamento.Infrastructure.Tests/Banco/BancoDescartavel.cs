using Microsoft.Data.SqlClient;
using Rastreamento.Infrastructure.Banco;

namespace Rastreamento.Infrastructure.Tests.Banco;

/// <summary>
/// Banco vazio, de nome único, criado para um teste e apagado no fim (também o <c>{Nome}_criando</c> da
/// criação de banco novo). Os dois projetos de teste o usam (o <c>Rastreamento.Api.Tests</c> o liga como fonte),
/// e é aqui que moram os apoios que os dois repetiriam: a raiz do repositório e o banco com schema e seed.
/// </summary>
public sealed class BancoDescartavel : IAsyncDisposable
{
  private const string ConexaoDoMaster =
      "Server=localhost,1433;Database=master;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True";

  public string Nome { get; }
  public string ConexaoAdministrativa => ConexaoDoMaster;

  private BancoDescartavel(string nome) => Nome = nome;

  public static Task<BancoDescartavel> CriarAsync() => CriarAsync($"RastruTeste_{Guid.NewGuid():N}");

  /// <summary>Cria com nome fixo (apaga antes um banco de mesmo nome que tenha sobrado).</summary>
  public static async Task<BancoDescartavel> CriarAsync(string nome)
  {
    var banco = new BancoDescartavel(nome);
    await banco.ApagarSeExistirAsync(nome);
    await ExecutarAsync($"CREATE DATABASE [{nome}]");
    return banco;
  }

  /// <summary>Só reserva um nome único (apagando o que sobrou com ele): quem cria o banco é o código sob teste.</summary>
  public static async Task<BancoDescartavel> ReservarNomeAsync()
  {
    var banco = new BancoDescartavel($"RastruTeste_{Guid.NewGuid():N}");
    await banco.ApagarSeExistirAsync(banco.Nome);
    await banco.ApagarSeExistirAsync(banco.Nome + "_criando");
    return banco;
  }

  /// <summary>Banco descartável com o <c>02</c> e o <c>db/seed.sql</c> (os sete usuários com a senha pública).</summary>
  public static async Task<BancoDescartavel> CriarComSeedAsync()
  {
    var banco = await CriarAsync();
    var scripts = new ScriptsDoBanco(RaizDoRepositorio());
    await using var conexao = new SqlConnection(banco.ConexaoDoBanco);
    await conexao.OpenAsync();
    foreach (var arquivo in new[] { scripts.Modelo, scripts.Seed })
      await ExecutorDeSql.ExecutarAsync(
          conexao, null, await File.ReadAllTextAsync(arquivo, System.Text.Encoding.UTF8), CancellationToken.None);
    return banco;
  }

  public static string RaizDoRepositorio()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rastreamento.slnx")))
      dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
  }

  /// <summary>Apaga um login SQL de teste, se existir (os bancos dele devem já ter sido apagados).</summary>
  public static async Task ApagarLoginAsync(string login)
  {
    await ExecutarAsync(
        $"IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{login}') DROP LOGIN [{login}]");
  }

  public string ConexaoDoBanco =>
      new SqlConnectionStringBuilder(ConexaoDoMaster) { InitialCatalog = Nome }.ConnectionString;

  public async ValueTask DisposeAsync()
  {
    // Os dois são tentados mesmo que o primeiro falhe: o _criando que sobrar contaminaria o teste seguinte.
    try
    {
      await ApagarSeExistirAsync(Nome);
    }
    finally
    {
      await ApagarSeExistirAsync(Nome + "_criando");
    }
  }

  private async Task ApagarSeExistirAsync(string nome)
  {
    await ExecutarAsync(
        $"IF DB_ID(N'{nome}') IS NOT NULL BEGIN " +
        $"ALTER DATABASE [{nome}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{nome}]; END");
  }

  private static async Task ExecutarAsync(string sql)
  {
    SqlConnection.ClearAllPools();
    await using var conexao = new SqlConnection(ConexaoDoMaster);
    await conexao.OpenAsync();
    await using var comando = new SqlCommand(sql, conexao) { CommandTimeout = 120 };
    await comando.ExecuteNonQueryAsync();
  }
}

using Microsoft.Data.SqlClient;

namespace Rastreamento.Infrastructure.Tests.Banco;

/// <summary>
/// Banco vazio, de nome único, criado para um teste e apagado no fim (também o <c>{Nome}_criando</c> da
/// criação de banco novo). Autocontido de propósito: só depende de <c>Microsoft.Data.SqlClient</c> e da BCL,
/// porque o <c>Rastreamento.Api.Tests</c> também o liga como fonte.
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

  public string ConexaoDoBanco =>
      new SqlConnectionStringBuilder(ConexaoDoMaster) { InitialCatalog = Nome }.ConnectionString;

  public async ValueTask DisposeAsync()
  {
    await ApagarSeExistirAsync(Nome);
    await ApagarSeExistirAsync(Nome + "_criando");
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

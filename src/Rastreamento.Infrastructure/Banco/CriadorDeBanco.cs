using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Rastreamento.Infrastructure.Banco;

/// <summary>
/// Cria o banco do ambiente de forma atômica (C11). Tudo acontece num banco de nome provisório
/// (<c>{nome}_criando</c>) e ele só ganha o nome final no último passo: banco interrompido no meio (segredo
/// errado, massa quebrada, container morto) nunca aparece com o nome final nem com os hashes do repositório.
/// Os arquivos já nascem com o nome final, para o renome não deixar <c>.mdf</c> chamado <c>_criando</c>.
/// </summary>
public sealed partial class CriadorDeBanco(SenhasDosUsuarios senhas)
{
  [GeneratedRegex("^[A-Za-z0-9_]{1,100}$")]
  private static partial Regex NomeValido();

  public async Task CriarAsync(
      string conexaoAdministrativa, string nome, bool vitrine,
      IReadOnlyDictionary<string, string> senhasDosUsuarios, ScriptsDoBanco scripts, CancellationToken ct)
  {
    // 1. Segredos conferidos antes de tocar no servidor.
    if (!NomeValido().IsMatch(nome))
      throw new ArgumentException("Nome de banco inválido: use letras, dígitos e sublinhado.", nameof(nome));
    ConferirSenhas(senhasDosUsuarios);

    var provisorio = nome + "_criando";
    var conexaoProvisoria = new SqlConnectionStringBuilder(conexaoAdministrativa) { InitialCatalog = provisorio }.ConnectionString;

    await using (var master = new SqlConnection(conexaoAdministrativa))
    {
      await master.OpenAsync(ct);
      if (await ExisteAsync(master, nome, ct))
        throw new InvalidOperationException($"O banco '{nome}' já existe; não se cria por cima.");

      // 2. Sobra de uma execução interrompida.
      await ApagarSeExistirAsync(master, provisorio, ct);

      // 3. Banco provisório, com os arquivos de nome final.
      await CriarProvisorioAsync(master, nome, ct);
    }

    await using (var conexao = new SqlConnection(conexaoProvisoria))
    {
      await conexao.OpenAsync(ct);

      // 4. Schema.
      await ExecutarArquivoAsync(conexao, null, scripts.Modelo, ct);

      // 5. Seed e senhas, ou nada: o hash do repositório nunca fica gravado fora desta transação.
      await using (var transacao = (SqlTransaction)await conexao.BeginTransactionAsync(ct))
      {
        try
        {
          await ExecutarArquivoAsync(conexao, transacao, scripts.Seed, ct);
          foreach (var (usuario, _) in SenhasDoSeed.Todas)
            await senhas.DefinirAsync(conexao, transacao, usuario, senhasDosUsuarios[usuario], ct);
          await transacao.CommitAsync(ct);
        }
        catch
        {
          try { await transacao.RollbackAsync(CancellationToken.None); }
          catch (InvalidOperationException) { /* a transação já foi encerrada pelo servidor */ }
          throw;
        }
      }

      // 6. Vitrine.
      if (vitrine)
      {
        await ExecutarArquivoAsync(conexao, null, scripts.SeedDemo, ct);
        await ExecutarArquivoAsync(conexao, null, scripts.MassaVitrine, ct);
      }

      // 7. Todas as migrações já estão no schema do 02.
      foreach (var migracao in scripts.Migracoes())
      {
        await using var anotar = new SqlCommand(
            "INSERT INTO dbo.MigracaoAplicada (Nome) VALUES (@nome)", conexao);
        anotar.Parameters.AddWithValue("@nome", migracao);
        await anotar.ExecuteNonQueryAsync(ct);
      }
    }

    // 8. Só aqui o banco ganha o nome final.
    SqlConnection.ClearAllPools();
    await using var final = new SqlConnection(conexaoAdministrativa);
    await final.OpenAsync(ct);
    await using var renomear = new SqlCommand(
        """
        DECLARE @sql NVARCHAR(MAX) = N'ALTER DATABASE ' + QUOTENAME(@provisorio)
                                   + N' MODIFY NAME = ' + QUOTENAME(@nome);
        EXEC (@sql);
        """, final);
    renomear.Parameters.AddWithValue("@provisorio", provisorio);
    renomear.Parameters.AddWithValue("@nome", nome);
    await renomear.ExecuteNonQueryAsync(ct);
  }

  private static void ConferirSenhas(IReadOnlyDictionary<string, string> senhasDosUsuarios)
  {
    var faltando = SenhasDoSeed.Todas
        .Where(s => !senhasDosUsuarios.TryGetValue(s.Usuario, out var v) || string.IsNullOrWhiteSpace(v))
        .Select(s => s.Usuario)
        .ToList();
    if (faltando.Count > 0)
      throw new InvalidOperationException(
          "Senha não configurada para: " + string.Join(", ", faltando) + " (variável Senhas__<usuario>).");

    var publicas = SenhasDoSeed.Todas
        .Where(s => senhasDosUsuarios[s.Usuario] == s.Senha)
        .Select(s => s.Usuario)
        .ToList();
    if (publicas.Count > 0)
      throw new InvalidOperationException(
          "Senha igual à pública do repositório para: " + string.Join(", ", publicas) + ".");
  }

  private static async Task<bool> ExisteAsync(SqlConnection master, string nome, CancellationToken ct)
  {
    await using var cmd = new SqlCommand("SELECT DB_ID(@nome)", master);
    cmd.Parameters.AddWithValue("@nome", nome);
    return await cmd.ExecuteScalarAsync(ct) is not (null or DBNull);
  }

  private static async Task ApagarSeExistirAsync(SqlConnection master, string nome, CancellationToken ct)
  {
    await using var cmd = new SqlCommand(
        """
        IF DB_ID(@nome) IS NOT NULL
        BEGIN
          DECLARE @sql NVARCHAR(MAX) = N'ALTER DATABASE ' + QUOTENAME(@nome)
              + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@nome);
          EXEC (@sql);
        END
        """, master) { CommandTimeout = 120 };
    cmd.Parameters.AddWithValue("@nome", nome);
    await cmd.ExecuteNonQueryAsync(ct);
  }

  private static async Task CriarProvisorioAsync(SqlConnection master, string nome, CancellationToken ct)
  {
    await using (var pasta = new SqlCommand(
        "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(512))", master))
    {
      var dados = await pasta.ExecuteScalarAsync(ct) as string
          ?? throw new InvalidOperationException("A instância não informou a pasta padrão de dados.");
      var separador = dados.Contains('\\') ? "\\" : "/";
      var raiz = dados.TrimEnd('\\', '/') + separador;

      await using var cmd = new SqlCommand(
          """
          DECLARE @sql NVARCHAR(MAX) = N'CREATE DATABASE ' + QUOTENAME(@nome + N'_criando')
              + N' ON PRIMARY (NAME = ' + QUOTENAME(@nome, '''')
              + N', FILENAME = N''' + REPLACE(@mdf, '''', '''''') + N''')'
              + N' LOG ON (NAME = ' + QUOTENAME(@nome + N'_log', '''')
              + N', FILENAME = N''' + REPLACE(@ldf, '''', '''''') + N''')';
          EXEC (@sql);
          """, master) { CommandTimeout = 600 };
      cmd.Parameters.AddWithValue("@nome", nome);
      cmd.Parameters.AddWithValue("@mdf", raiz + nome + ".mdf");
      cmd.Parameters.AddWithValue("@ldf", raiz + nome + "_log.ldf");
      await cmd.ExecuteNonQueryAsync(ct);
    }
  }

  private static async Task ExecutarArquivoAsync(
      SqlConnection conexao, SqlTransaction? transacao, string caminho, CancellationToken ct) =>
      await ExecutorDeSql.ExecutarAsync(conexao, transacao, await File.ReadAllTextAsync(caminho, Encoding.UTF8, ct), ct);
}

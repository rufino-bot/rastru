using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Rastreamento.Infrastructure.Banco;

/// <summary>
/// Garante o login SQL do ambiente (C17): dono só do próprio banco, com <c>master</c> como banco padrão. O nome e
/// a senha entram no T-SQL dinâmico por <c>QUOTENAME</c> <b>dentro do T-SQL</b>, a partir de parâmetros, nunca por
/// concatenação em C#.
/// </summary>
public sealed partial class LoginDoAmbiente
{
  // QUOTENAME devolve NULL para entrada acima de 128 caracteres, e o EXEC de um NULL passaria em silêncio.
  private const int LimiteDoQuotename = 128;

  [GeneratedRegex("^[a-z_][a-z0-9_]*$")]
  private static partial Regex NomeValido();

  public async Task GarantirAsync(
      string conexaoAdministrativa, string banco, string login, string senha, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(login) || login.Length > LimiteDoQuotename || !NomeValido().IsMatch(login))
      throw new ArgumentException("Nome de login inválido: use minúsculas, dígitos e sublinhado.", nameof(login));
    if (string.IsNullOrEmpty(senha))
      throw new ArgumentException("A senha do login não pode ser vazia.", nameof(senha));
    if (senha.Length > LimiteDoQuotename)
      throw new ArgumentException($"A senha do login passa de {LimiteDoQuotename} caracteres.", nameof(senha));

    await using (var master = new SqlConnection(conexaoAdministrativa))
    {
      await master.OpenAsync(ct);
      await using var cmd = new SqlCommand(
          """
          DECLARE @sql NVARCHAR(MAX);
          IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @login)
            SET @sql = N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@senha, '''')
                     + N', DEFAULT_DATABASE = [master]';
          ELSE
            SET @sql = N'ALTER LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@senha, '''')
                     + N', DEFAULT_DATABASE = [master]';
          EXEC (@sql);
          """, master);
      cmd.Parameters.AddWithValue("@login", login);
      cmd.Parameters.AddWithValue("@senha", senha);
      await cmd.ExecuteNonQueryAsync(ct);
    }

    var conexaoDoBanco = new SqlConnectionStringBuilder(conexaoAdministrativa) { InitialCatalog = banco }.ConnectionString;
    await using var conexao = new SqlConnection(conexaoDoBanco);
    await conexao.OpenAsync(ct);
    await using var usuario = new SqlCommand(
        """
        DECLARE @sql NVARCHAR(MAX);
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @login)
          SET @sql = N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login);
        ELSE
          SET @sql = N'ALTER USER ' + QUOTENAME(@login) + N' WITH LOGIN = ' + QUOTENAME(@login);
        EXEC (@sql);
        SET @sql = N'ALTER ROLE db_owner ADD MEMBER ' + QUOTENAME(@login);
        EXEC (@sql);
        """, conexao);
    usuario.Parameters.AddWithValue("@login", login);
    await usuario.ExecuteNonQueryAsync(ct);
  }
}

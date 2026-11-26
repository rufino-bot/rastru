using Microsoft.Data.SqlClient;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Infrastructure.Banco;

/// <summary>
/// Define a senha de um usuário e confere quais ainda têm a senha pública do seed. SQL parametrizado, sem EF,
/// porque roda como comando de linha antes de a API existir. Nunca devolve nem registra senha ou hash.
/// </summary>
public sealed class SenhasDosUsuarios(IPasswordHasher hasher)
{
  public async Task DefinirAsync(
      SqlConnection conexao, SqlTransaction? transacao, string usuario, string senha, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(senha))
      throw new ArgumentException("A senha não pode ser vazia.", nameof(senha));
    if (SenhasDoSeed.Todas.Any(s =>
            string.Equals(s.Usuario, usuario, StringComparison.OrdinalIgnoreCase) && s.Senha == senha))
      throw new ArgumentException("Essa é a senha pública do repositório para esse usuário.", nameof(senha));

    var hash = hasher.Hash(senha);
    await using var comando = new SqlCommand(
        "UPDATE dbo.Usuario SET SenhaHash = @hash, FalhasConsecutivas = 0, BloqueadoAte = NULL " +
        "WHERE NomeUsuario = @usuario", conexao, transacao);
    comando.Parameters.AddWithValue("@hash", hash);
    comando.Parameters.AddWithValue("@usuario", usuario);
    if (await comando.ExecuteNonQueryAsync(ct) == 0)
      throw new InvalidOperationException($"Usuário '{usuario}' não existe.");
  }

  /// <summary>Os nomes, entre os sete do seed, que existem no banco com hash que verifica a senha do seed.</summary>
  public async Task<IReadOnlyList<string>> ComSenhaDoSeedAsync(SqlConnection conexao, CancellationToken ct)
  {
    var achados = new List<string>();
    foreach (var (usuario, senha) in SenhasDoSeed.Todas)
    {
      await using var comando = new SqlCommand(
          "SELECT SenhaHash FROM dbo.Usuario WHERE NomeUsuario = @usuario", conexao);
      comando.Parameters.AddWithValue("@usuario", usuario);
      if (await comando.ExecuteScalarAsync(ct) is not string hash) continue;
      if (Confere(senha, hash)) achados.Add(usuario);
    }
    return achados;
  }

  private bool Confere(string senha, string hash)
  {
    try
    {
      return hasher.Verificar(senha, hash);
    }
    catch (Exception)
    {
      // Hash que não é BCrypt válido não é a senha do seed.
      return false;
    }
  }
}

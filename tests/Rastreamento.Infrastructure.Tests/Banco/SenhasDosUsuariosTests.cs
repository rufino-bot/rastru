using Microsoft.Data.SqlClient;
using Rastreamento.Infrastructure.Banco;
using Rastreamento.Infrastructure.Security;

namespace Rastreamento.Infrastructure.Tests.Banco;

public class SenhasDosUsuariosTests
{
  private static readonly BCryptPasswordHasher Hasher = new();

  [Fact]
  public async Task A_lista_do_seed_bate_com_o_seed()
  {
    await using var banco = await BancoDescartavel.CriarComSeedAsync();
    await using var conexao = await AbrirAsync(banco);

    var com = await new SenhasDosUsuarios(Hasher).ComSenhaDoSeedAsync(conexao, CancellationToken.None);

    Assert.Equal(SenhasDoSeed.Todas.Select(s => s.Usuario).Order(), com.Order());
    Assert.Equal(7, com.Count);
  }

  [Fact]
  public async Task Definir_troca_o_hash_e_tira_o_usuario_da_lista()
  {
    await using var banco = await BancoDescartavel.CriarComSeedAsync();
    await using var conexao = await AbrirAsync(banco);
    var senhas = new SenhasDosUsuarios(Hasher);

    await senhas.DefinirAsync(conexao, null, "admin", "Outra-Senha-forte-1", CancellationToken.None);

    Assert.True(Hasher.Verificar("Outra-Senha-forte-1", await HashDe(conexao, "admin")));
    var com = await senhas.ComSenhaDoSeedAsync(conexao, CancellationToken.None);
    Assert.DoesNotContain("admin", com);
    Assert.Equal(6, com.Count);
  }

  [Fact]
  public async Task Definir_zera_a_trava()
  {
    await using var banco = await BancoDescartavel.CriarComSeedAsync();
    await using var conexao = await AbrirAsync(banco);
    await using (var trava = new SqlCommand(
        "UPDATE dbo.Usuario SET FalhasConsecutivas = 5, BloqueadoAte = DATEADD(HOUR, 1, SYSUTCDATETIME()) " +
        "WHERE NomeUsuario = 'admin'", conexao))
      await trava.ExecuteNonQueryAsync();

    await new SenhasDosUsuarios(Hasher).DefinirAsync(conexao, null, "admin", "Outra-Senha-forte-1", CancellationToken.None);

    await using var cmd = new SqlCommand(
        "SELECT FalhasConsecutivas, BloqueadoAte FROM dbo.Usuario WHERE NomeUsuario = 'admin'", conexao);
    await using var r = await cmd.ExecuteReaderAsync();
    Assert.True(await r.ReadAsync());
    Assert.Equal(0, r.GetInt32(0));
    Assert.True(r.IsDBNull(1));
  }

  [Theory]
  [InlineData("Admin@123")]
  [InlineData("")]
  [InlineData("   ")]
  public async Task Definir_recusa_a_senha_do_seed_e_a_vazia(string senha)
  {
    await using var banco = await BancoDescartavel.CriarComSeedAsync();
    await using var conexao = await AbrirAsync(banco);

    await Assert.ThrowsAsync<ArgumentException>(() =>
        new SenhasDosUsuarios(Hasher).DefinirAsync(conexao, null, "admin", senha, CancellationToken.None));
  }

  [Fact]
  public async Task Definir_usuario_inexistente_lanca()
  {
    await using var banco = await BancoDescartavel.CriarComSeedAsync();
    await using var conexao = await AbrirAsync(banco);

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
        new SenhasDosUsuarios(Hasher).DefinirAsync(conexao, null, "ninguem", "Outra-Senha-forte-1", CancellationToken.None));
  }

  private static async Task<SqlConnection> AbrirAsync(BancoDescartavel banco)
  {
    var conexao = new SqlConnection(banco.ConexaoDoBanco);
    await conexao.OpenAsync();
    return conexao;
  }

  private static async Task<string> HashDe(SqlConnection conexao, string usuario)
  {
    await using var cmd = new SqlCommand("SELECT SenhaHash FROM dbo.Usuario WHERE NomeUsuario = @u", conexao);
    cmd.Parameters.AddWithValue("@u", usuario);
    return (string)(await cmd.ExecuteScalarAsync())!;
  }
}

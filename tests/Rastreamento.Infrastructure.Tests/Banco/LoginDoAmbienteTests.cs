using Microsoft.Data.SqlClient;
using Rastreamento.Infrastructure.Banco;

namespace Rastreamento.Infrastructure.Tests.Banco;

public class LoginDoAmbienteTests : IAsyncLifetime
{
  private readonly string _login = $"rastru_teste_{Guid.NewGuid():N}";
  private BancoDescartavel _a = null!;
  private BancoDescartavel _b = null!;

  public async Task InitializeAsync()
  {
    _a = await BancoDescartavel.CriarComSeedAsync();
    _b = await BancoDescartavel.CriarAsync();
  }

  public async Task DisposeAsync()
  {
    await _a.DisposeAsync();
    await _b.DisposeAsync();
    await BancoDescartavel.ApagarLoginAsync(_login);
  }

  private string ConexaoDoLogin(BancoDescartavel banco, string senha) =>
      new SqlConnectionStringBuilder(banco.ConexaoDoBanco)
      { UserID = _login, Password = senha, Pooling = false }.ConnectionString;

  [Fact]
  public async Task O_login_alcanca_so_o_proprio_banco()
  {
    var senha = $"Forte-{Guid.NewGuid():N}-Aa1!";
    await new LoginDoAmbiente().GarantirAsync(_a.ConexaoAdministrativa, _a.Nome, _login, senha, CancellationToken.None);

    await using (var conexao = new SqlConnection(ConexaoDoLogin(_a, senha)))
    {
      await conexao.OpenAsync();
      await using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.Usuario", conexao);
      Assert.Equal(7, (int)(await cmd.ExecuteScalarAsync())!);
    }

    await using var emB = new SqlConnection(ConexaoDoLogin(_b, senha));
    await Assert.ThrowsAsync<SqlException>(() => emB.OpenAsync());
  }

  [Fact]
  public async Task Garantir_de_novo_troca_a_senha()
  {
    var antiga = $"Antiga-{Guid.NewGuid():N}-Aa1!";
    var nova = $"Nova-{Guid.NewGuid():N}-Aa1!";
    var login = new LoginDoAmbiente();
    await login.GarantirAsync(_a.ConexaoAdministrativa, _a.Nome, _login, antiga, CancellationToken.None);

    await login.GarantirAsync(_a.ConexaoAdministrativa, _a.Nome, _login, nova, CancellationToken.None);

    await using var velha = new SqlConnection(ConexaoDoLogin(_a, antiga));
    await Assert.ThrowsAsync<SqlException>(() => velha.OpenAsync());
    await using var atual = new SqlConnection(ConexaoDoLogin(_a, nova));
    await atual.OpenAsync();
  }

  [Theory]
  [InlineData("x]; DROP")]
  [InlineData("Rastru")]
  public async Task Nome_de_login_invalido_lanca(string nome)
  {
    await Assert.ThrowsAsync<ArgumentException>(() => new LoginDoAmbiente().GarantirAsync(
        _a.ConexaoAdministrativa, _a.Nome, nome, $"Forte-{Guid.NewGuid():N}-Aa1!", CancellationToken.None));
  }
}

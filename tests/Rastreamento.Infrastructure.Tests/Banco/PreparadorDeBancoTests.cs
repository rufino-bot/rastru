using Microsoft.Data.SqlClient;
using Rastreamento.Infrastructure.Banco;
using Rastreamento.Infrastructure.Security;

namespace Rastreamento.Infrastructure.Tests.Banco;

public class PreparadorDeBancoTests : IAsyncLifetime
{
  private readonly string _login = $"rastru_teste_{Guid.NewGuid():N}";
  private readonly List<string> _raizes = [];
  private BancoDescartavel _banco = null!;

  public async Task InitializeAsync() => _banco = await BancoDescartavel.ReservarNomeAsync();

  public async Task DisposeAsync()
  {
    await _banco.DisposeAsync();
    await BancoDescartavel.ApagarLoginAsync(_login);
    foreach (var raiz in _raizes.Where(Directory.Exists)) Directory.Delete(raiz, recursive: true);
  }

  private static PreparadorDeBanco Preparador() =>
      new(new CriadorDeBanco(new SenhasDosUsuarios(new BCryptPasswordHasher())),
          new MigradorDeBanco(new BackupDoBanco()), new LoginDoAmbiente());

  private OpcoesDoBanco Opcoes(string raiz, string senhaDoLogin) => new()
  {
    ConexaoAdministrativa = _banco.ConexaoAdministrativa,
    Nome = _banco.Nome,
    Login = _login,
    SenhaDoLogin = senhaDoLogin,
    RaizDosScripts = raiz,
  };

  private static Dictionary<string, string> Senhas() =>
      SenhasDoSeed.Todas.ToDictionary(s => s.Usuario, s => $"Forte-{s.Usuario}-{Guid.NewGuid():N}-Aa1!");

  [Theory]
  [InlineData("sa", "Forte-Aa1!-senha-boa")]
  [InlineData("Rastru", "Forte-Aa1!-senha-boa")]
  [InlineData("rastru_ok", "x")]
  public async Task Login_invalido_aborta_antes_de_criar_o_banco(string login, string senha)
  {
    if (senha == "x") senha = new string('a', 129);
    var opcoes = Opcoes(BancoDescartavel.RaizDoRepositorio(), senha);
    opcoes.Login = login;

    await Assert.ThrowsAsync<ArgumentException>(() =>
        Preparador().PrepararAsync(opcoes, Senhas(), CancellationToken.None));

    await using var master = new SqlConnection(_banco.ConexaoAdministrativa);
    await master.OpenAsync();
    await using var cmd = new SqlCommand(
        "SELECT COUNT(*) FROM sys.databases WHERE name IN (@n, @c)", master);
    cmd.Parameters.AddWithValue("@n", _banco.Nome);
    cmd.Parameters.AddWithValue("@c", _banco.Nome + "_criando");
    Assert.Equal(0, (int)(await cmd.ExecuteScalarAsync())!);
  }

  [Fact]
  public async Task Banco_inexistente_e_criado_e_o_login_garantido()
  {
    var senha = $"Forte-{Guid.NewGuid():N}-Aa1!";

    var resultado = await Preparador().PrepararAsync(
        Opcoes(BancoDescartavel.RaizDoRepositorio(), senha), Senhas(), CancellationToken.None);

    Assert.True(resultado.BancoNovo);
    var conexaoDoLogin = new SqlConnectionStringBuilder(_banco.ConexaoDoBanco)
    { UserID = _login, Password = senha, Pooling = false }.ConnectionString;
    await using var conexao = new SqlConnection(conexaoDoLogin);
    await conexao.OpenAsync();
    await using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.Usuario", conexao);
    Assert.Equal(7, (int)(await cmd.ExecuteScalarAsync())!);
  }

  [Fact]
  public async Task Banco_existente_e_migrado_sem_recriar()
  {
    var repo = BancoDescartavel.RaizDoRepositorio();
    var raiz = Path.Combine(Path.GetTempPath(), "rastru-preparador-" + Guid.NewGuid().ToString("N"));
    _raizes.Add(raiz);
    Directory.CreateDirectory(Path.Combine(raiz, "specs"));
    Directory.CreateDirectory(Path.Combine(raiz, "db", "migracoes"));
    File.Copy(Path.Combine(repo, "specs", "02-modelo-de-dados.sql"), Path.Combine(raiz, "specs", "02-modelo-de-dados.sql"));
    File.Copy(Path.Combine(repo, "db", "seed.sql"), Path.Combine(raiz, "db", "seed.sql"));
    foreach (var f in Directory.GetFiles(Path.Combine(repo, "db", "migracoes")))
      File.Copy(f, Path.Combine(raiz, "db", "migracoes", Path.GetFileName(f)));
    var opcoes = Opcoes(raiz, $"Forte-{Guid.NewGuid():N}-Aa1!");
    var senhas = Senhas();

    var primeiro = await Preparador().PrepararAsync(opcoes, senhas, CancellationToken.None);
    Assert.True(primeiro.BancoNovo);
    await using (var conexao = new SqlConnection(_banco.ConexaoDoBanco))
    {
      await conexao.OpenAsync();
      await using var cmd = new SqlCommand(
          "INSERT INTO dbo.Setor (Nome, Ativo) VALUES (N'Setor de antes', 1)", conexao);
      await cmd.ExecuteNonQueryAsync();
    }

    File.WriteAllText(Path.Combine(raiz, "db", "migracoes", "0002-teste.sql"),
        "CREATE TABLE dbo.TabelaDeTeste (Id INT NOT NULL);");
    var segundo = await Preparador().PrepararAsync(opcoes, senhas, CancellationToken.None);

    Assert.False(segundo.BancoNovo);
    Assert.Equal(["0002-teste.sql"], segundo.MigracoesAplicadas);
    Assert.False(string.IsNullOrEmpty(segundo.Backup));
    await using var verificacao = new SqlConnection(_banco.ConexaoDoBanco);
    await verificacao.OpenAsync();
    await using var conta = new SqlCommand(
        "SELECT COUNT(*) FROM dbo.Setor WHERE Nome = N'Setor de antes'", verificacao);
    Assert.Equal(1, (int)(await conta.ExecuteScalarAsync())!);
    await using var tabela = new SqlCommand("SELECT OBJECT_ID('dbo.TabelaDeTeste')", verificacao);
    Assert.False((await tabela.ExecuteScalarAsync()) is null or DBNull);
  }
}

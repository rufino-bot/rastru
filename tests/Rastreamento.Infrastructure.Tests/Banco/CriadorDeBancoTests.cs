using Microsoft.Data.SqlClient;
using Rastreamento.Infrastructure.Banco;
using Rastreamento.Infrastructure.Security;

namespace Rastreamento.Infrastructure.Tests.Banco;

public class CriadorDeBancoTests
{
  private static readonly BCryptPasswordHasher Hasher = new();
  private static readonly string[] Sete = SenhasDoSeed.Todas.Select(s => s.Usuario).ToArray();

  private static CriadorDeBanco Criador() => new(new SenhasDosUsuarios(Hasher));

  private static Dictionary<string, string> SenhasFortes() =>
      Sete.ToDictionary(u => u, u => $"Forte-{u}-{Guid.NewGuid():N}-Aa1!");

  [Fact]
  public async Task Banco_novo_tem_schema_perfis_e_os_sete_usuarios_com_a_senha_do_segredo()
  {
    await using var banco = await BancoDescartavel.ReservarNomeAsync();
    var senhas = SenhasFortes();

    await Criador().CriarAsync(banco.ConexaoAdministrativa, banco.Nome, false, senhas,
        new ScriptsDoBanco(BancoDescartavel.RaizDoRepositorio()), CancellationToken.None);

    await using var conexao = new SqlConnection(banco.ConexaoDoBanco);
    await conexao.OpenAsync();
    foreach (var usuario in Sete)
      Assert.True(Hasher.Verificar(senhas[usuario], await Escalar<string>(conexao,
          $"SELECT SenhaHash FROM dbo.Usuario WHERE NomeUsuario = '{usuario}'")), usuario);
    Assert.Empty(await new SenhasDosUsuarios(Hasher).ComSenhaDoSeedAsync(conexao, CancellationToken.None));
    Assert.Equal(7, await Escalar<int>(conexao, "SELECT COUNT(*) FROM dbo.Perfil"));
  }

  [Fact]
  public async Task Banco_novo_anota_todas_as_migracoes_como_aplicadas()
  {
    await using var banco = await BancoDescartavel.ReservarNomeAsync();
    var scripts = new ScriptsDoBanco(BancoDescartavel.RaizDoRepositorio());

    await Criador().CriarAsync(banco.ConexaoAdministrativa, banco.Nome, false, SenhasFortes(), scripts,
        CancellationToken.None);

    await using var conexao = new SqlConnection(banco.ConexaoDoBanco);
    await conexao.OpenAsync();
    await using var cmd = new SqlCommand("SELECT Nome FROM dbo.MigracaoAplicada ORDER BY Nome", conexao);
    var anotadas = new List<string>();
    await using (var r = await cmd.ExecuteReaderAsync())
      while (await r.ReadAsync()) anotadas.Add(r.GetString(0));
    Assert.Equal(scripts.Migracoes(), anotadas);
  }

  [Fact]
  public async Task Banco_novo_sem_vitrine_nao_tem_pedido()
  {
    await using var banco = await BancoDescartavel.ReservarNomeAsync();

    await Criador().CriarAsync(banco.ConexaoAdministrativa, banco.Nome, false, SenhasFortes(),
        new ScriptsDoBanco(BancoDescartavel.RaizDoRepositorio()), CancellationToken.None);

    await using var conexao = new SqlConnection(banco.ConexaoDoBanco);
    await conexao.OpenAsync();
    Assert.Equal(0, await Escalar<int>(conexao, "SELECT COUNT(*) FROM dbo.Pedido"));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task Senha_faltando_aborta_antes_de_criar(bool vazia)
  {
    await using var banco = await BancoDescartavel.ReservarNomeAsync();
    var senhas = SenhasFortes();
    if (vazia) senhas["gestao"] = "";
    else senhas.Remove("gestao");

    var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Criador().CriarAsync(
        banco.ConexaoAdministrativa, banco.Nome, false, senhas,
        new ScriptsDoBanco(BancoDescartavel.RaizDoRepositorio()), CancellationToken.None));

    Assert.Contains("gestao", e.Message);
    Assert.False(await Existe(banco, banco.Nome));
    Assert.False(await Existe(banco, banco.Nome + "_criando"));
  }

  [Fact]
  public async Task Criacao_interrompida_nao_deixa_banco_com_o_nome_final()
  {
    await using var banco = await BancoDescartavel.ReservarNomeAsync();
    var raiz = Path.Combine(Path.GetTempPath(), "rastru-criador-" + Guid.NewGuid().ToString("N"));
    try
    {
      var repo = BancoDescartavel.RaizDoRepositorio();
      Directory.CreateDirectory(Path.Combine(raiz, "specs"));
      Directory.CreateDirectory(Path.Combine(raiz, "db", "migracoes"));
      File.Copy(Path.Combine(repo, "specs", "02-modelo-de-dados.sql"), Path.Combine(raiz, "specs", "02-modelo-de-dados.sql"));
      foreach (var f in new[] { "seed.sql", "seed-demo.sql" })
        File.Copy(Path.Combine(repo, "db", f), Path.Combine(raiz, "db", f));
      foreach (var f in Directory.GetFiles(Path.Combine(repo, "db", "migracoes")))
        File.Copy(f, Path.Combine(raiz, "db", "migracoes", Path.GetFileName(f)));
      File.WriteAllText(Path.Combine(raiz, "db", "massa-vitrine.sql"), "THROW 50000, 'quebrada', 1;");
      var senhas = SenhasFortes();

      await Assert.ThrowsAnyAsync<SqlException>(() => Criador().CriarAsync(
          banco.ConexaoAdministrativa, banco.Nome, true, senhas, new ScriptsDoBanco(raiz), CancellationToken.None));

      Assert.False(await Existe(banco, banco.Nome));

      await Criador().CriarAsync(banco.ConexaoAdministrativa, banco.Nome, false, senhas,
          new ScriptsDoBanco(repo), CancellationToken.None);

      Assert.True(await Existe(banco, banco.Nome));
      Assert.False(await Existe(banco, banco.Nome + "_criando"));
    }
    finally
    {
      if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true);
    }
  }

  private static async Task<bool> Existe(BancoDescartavel banco, string nome)
  {
    await using var conexao = new SqlConnection(banco.ConexaoAdministrativa);
    await conexao.OpenAsync();
    return await Escalar<int>(conexao, $"SELECT CASE WHEN DB_ID(N'{nome}') IS NULL THEN 0 ELSE 1 END") == 1;
  }

  private static async Task<T> Escalar<T>(SqlConnection conexao, string sql)
  {
    await using var cmd = new SqlCommand(sql, conexao);
    var valor = await cmd.ExecuteScalarAsync();
    return valor is null or DBNull ? default! : (T)valor;
  }
}

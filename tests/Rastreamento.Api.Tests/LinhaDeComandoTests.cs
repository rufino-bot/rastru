using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Rastreamento.Api.Comandos;
using Rastreamento.Infrastructure.Banco;
using Rastreamento.Infrastructure.Tests.Banco;

namespace Rastreamento.Api.Tests;

public class LinhaDeComandoTests
{
  private const string ConexaoDoBancoCompartilhado =
      "Server=localhost,1433;Database=Rastreamento;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True";

  private static readonly string[] Sete =
      ["admin", "pcp", "operador", "movimentador", "separador", "qualidade", "gestao"];

  [Theory]
  [InlineData("banco", "xyz")]
  [InlineData("definir-senha")]
  public async Task Comando_desconhecido_sai_2(params string[] args)
  {
    var (codigo, saida, erro) = await Rodar(args, new Dictionary<string, string?>());

    Assert.Equal(2, codigo);
    Assert.NotEmpty(saida + erro);
  }

  [Fact]
  public async Task Conferir_no_banco_de_teste_acusa_os_sete()
  {
    var (codigo, saida, erro) = await Rodar(
        ["conferir-senhas"], new() { ["ConnectionStrings:Rastreamento"] = ConexaoDoBancoCompartilhado });

    Assert.Equal(1, codigo);
    foreach (var nome in Sete) Assert.Contains(nome, saida + erro);
    SemSegredos(saida + erro);
  }

  [Fact]
  public async Task Definir_sem_a_senha_configurada_sai_1()
  {
    await using var banco = await BancoDescartavel.CriarComSeedAsync();

    var (codigo, saida, erro) = await Rodar(
        ["definir-senha", "admin"], new() { ["ConnectionStrings:Rastreamento"] = banco.ConexaoDoBanco });

    Assert.Equal(1, codigo);
    Assert.Contains("Senhas__admin", saida + erro);
    SemSegredos(saida + erro);
  }

  [Fact]
  public async Task Definir_e_depois_conferir()
  {
    await using var banco = await BancoDescartavel.CriarComSeedAsync();
    var config = new Dictionary<string, string?> { ["ConnectionStrings:Rastreamento"] = banco.ConexaoDoBanco };
    var todas = new System.Text.StringBuilder();

    foreach (var nome in Sete)
    {
      config[$"Senhas:{nome}"] = $"Senha-nova-de-{nome}-9!";
      var (codigo, saida, erro) = await Rodar(["definir-senha", nome], config);
      Assert.Equal(0, codigo);
      todas.Append(saida).Append(erro);
    }
    var (c, s, e) = await Rodar(["conferir-senhas"], config);
    todas.Append(s).Append(e);

    Assert.Equal(0, c);
    Assert.Contains("Nenhum dos sete usuários do seed tem a senha do repositório.", s);
    foreach (var nome in Sete) Assert.DoesNotContain($"Senha-nova-de-{nome}-9!", todas.ToString());
    SemSegredos(todas.ToString());
  }

  [Fact]
  public async Task Saida_nunca_tem_a_senha_nem_a_conexao()
  {
    await using var banco = await BancoDescartavel.CriarComSeedAsync();
    var config = new Dictionary<string, string?>
    {
      ["ConnectionStrings:Rastreamento"] = banco.ConexaoDoBanco,
      ["Senhas:admin"] = "Admin@123", // recusada: é a do seed
    };

    var (codigo, saida, erro) = await Rodar(["definir-senha", "admin"], config);

    Assert.Equal(1, codigo);
    Assert.DoesNotContain("Admin@123", saida + erro);
    SemSegredos(saida + erro);

    config["Senhas:admin"] = "Uma-Senha-Segura-77";
    config["ConnectionStrings:Rastreamento"] = new SqlConnectionStringBuilder(banco.ConexaoDoBanco) { InitialCatalog = "nao_existe_" + banco.Nome }.ConnectionString;
    (codigo, saida, erro) = await Rodar(["definir-senha", "admin"], config);
    Assert.Equal(1, codigo);
    Assert.DoesNotContain("Uma-Senha-Segura-77", saida + erro);
    SemSegredos(saida + erro);
  }

  [Fact]
  public async Task Banco_preparar_cria_e_conferir_passa()
  {
    await using var banco = await BancoDescartavel.ReservarNomeAsync();
    var login = $"rastru_teste_{Guid.NewGuid():N}";
    var senhaDoLogin = $"Forte-{Guid.NewGuid():N}-Aa1!";
    try
    {
      var config = new Dictionary<string, string?>
      {
        ["Banco:ConexaoAdministrativa"] = banco.ConexaoAdministrativa,
        ["Banco:Nome"] = banco.Nome,
        ["Banco:Login"] = login,
        ["Banco:SenhaDoLogin"] = senhaDoLogin,
        ["Banco:Vitrine"] = "false",
        ["Banco:RaizDosScripts"] = BancoDescartavel.RaizDoRepositorio(),
        ["ConnectionStrings:Rastreamento"] = new SqlConnectionStringBuilder(banco.ConexaoDoBanco)
        { UserID = login, Password = senhaDoLogin }.ConnectionString,
      };
      foreach (var nome in Sete) config[$"Senhas:{nome}"] = $"Forte-{nome}-{Guid.NewGuid():N}-Aa1!";

      var (codigo, saida, erro) = await Rodar(["banco", "preparar"], config);
      Assert.Equal(0, codigo);
      Assert.Contains("banco novo criado", saida);
      Assert.Contains($"login {login} garantido", saida);
      SemSegredos(saida + erro);
      Assert.DoesNotContain(senhaDoLogin, saida + erro);

      var (c2, s2, e2) = await Rodar(["conferir-senhas"], config);
      Assert.Equal(0, c2);
      SemSegredos(s2 + e2);

      var (c3, s3, e3) = await Rodar(["banco", "preparar"], config);
      Assert.Equal(0, c3);
      Assert.Contains("nada a migrar", s3);
      SemSegredos(s3 + e3);
    }
    finally
    {
      await banco.DisposeAsync();
      await BancoDescartavel.ApagarLoginAsync(login);
    }
  }

  private static void SemSegredos(string texto)
  {
    Assert.DoesNotContain("Password=", texto, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("$2a$", texto);
    Assert.DoesNotContain("Your_strong_Pass123", texto);
  }

  private static async Task<(int Codigo, string Saida, string Erro)> Rodar(
      string[] args, Dictionary<string, string?> config)
  {
    var configuracao = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
    var saida = new StringWriter();
    var erro = new StringWriter();
    var codigo = await LinhaDeComando.ExecutarAsync(args, configuracao, saida, erro);
    return (codigo, saida.ToString(), erro.ToString());
  }
}

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
    await using var banco = await BancoComSeedAsync();

    var (codigo, saida, erro) = await Rodar(
        ["definir-senha", "admin"], new() { ["ConnectionStrings:Rastreamento"] = banco.ConexaoDoBanco });

    Assert.Equal(1, codigo);
    Assert.Contains("Senhas__admin", saida + erro);
    SemSegredos(saida + erro);
  }

  [Fact]
  public async Task Definir_e_depois_conferir()
  {
    await using var banco = await BancoComSeedAsync();
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
    await using var banco = await BancoComSeedAsync();
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

  private static async Task<BancoDescartavel> BancoComSeedAsync()
  {
    var banco = await BancoDescartavel.CriarAsync();
    var scripts = new ScriptsDoBanco(RaizDoRepositorio());
    await using var conexao = new SqlConnection(banco.ConexaoDoBanco);
    await conexao.OpenAsync();
    foreach (var arquivo in new[] { scripts.Modelo, scripts.Seed })
      await ExecutorDeSql.ExecutarAsync(
          conexao, null, await File.ReadAllTextAsync(arquivo, System.Text.Encoding.UTF8), CancellationToken.None);
    return banco;
  }

  private static string RaizDoRepositorio()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rastreamento.slnx")))
      dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
  }
}

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Rastreamento.Infrastructure.Banco;
using Rastreamento.Infrastructure.Security;

namespace Rastreamento.Api.Comandos;

/// <summary>
/// Os verbos de linha de comando do binário da API (<c>definir-senha</c>, <c>conferir-senhas</c> e, na Task 6,
/// <c>banco preparar</c>): rodam antes do <c>CreateBuilder</c>, sem subir o servidor. Códigos de saída: 0 sucesso,
/// 1 falha, 2 uso errado. A saída nunca traz senha, hash nem a cadeia de conexão.
/// </summary>
public static class LinhaDeComando
{
  private static readonly string[] Verbos = ["banco", "definir-senha", "conferir-senhas"];

  public static bool EhComando(string[] args) => args.Length > 0 && Verbos.Contains(args[0]);

  public static Task<int> ExecutarAsync(string[] args)
  {
    var ambiente = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    var configuracao = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{ambiente}.json", optional: true)
        .AddEnvironmentVariables()
        .Build();
    return ExecutarAsync(args, configuracao, Console.Out, Console.Error);
  }

  public static async Task<int> ExecutarAsync(
      string[] args, IConfiguration configuracao, TextWriter saida, TextWriter erro)
  {
    if (!EhComando(args))
    {
      await erro.WriteLineAsync("Comando desconhecido.");
      return 2;
    }

    return args[0] switch
    {
      "definir-senha" => await DefinirSenhaAsync(args, configuracao, saida, erro),
      "conferir-senhas" => await ConferirSenhasAsync(args, configuracao, saida, erro),
      _ => await SubcomandoDesconhecidoAsync(args, erro),
    };
  }

  private static async Task<int> SubcomandoDesconhecidoAsync(string[] args, TextWriter erro)
  {
    // O "banco preparar" entra na Task 6; até lá todo "banco ..." é subcomando desconhecido.
    await erro.WriteLineAsync($"Uso: subcomando desconhecido para '{args[0]}'.");
    return 2;
  }

  private static async Task<int> DefinirSenhaAsync(
      string[] args, IConfiguration configuracao, TextWriter saida, TextWriter erro)
  {
    if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
    {
      await erro.WriteLineAsync("Uso: definir-senha <usuario>  (a senha vem de Senhas__<usuario>).");
      return 2;
    }

    var usuario = args[1];
    var senha = configuracao[$"Senhas:{usuario}"];
    if (string.IsNullOrWhiteSpace(senha))
    {
      await erro.WriteLineAsync($"Senha não configurada: defina a variável de ambiente Senhas__{usuario}.");
      return 1;
    }

    var conexaoTexto = configuracao.GetConnectionString("Rastreamento");
    if (string.IsNullOrWhiteSpace(conexaoTexto))
    {
      await erro.WriteLineAsync("Conexão não configurada: defina ConnectionStrings__Rastreamento.");
      return 1;
    }

    try
    {
      await using var conexao = new SqlConnection(conexaoTexto);
      await conexao.OpenAsync();
      await new SenhasDosUsuarios(new BCryptPasswordHasher())
          .DefinirAsync(conexao, null, usuario, senha, CancellationToken.None);
    }
    catch (ArgumentException)
    {
      await erro.WriteLineAsync($"Senha recusada para '{usuario}': vazia ou igual à senha pública do repositório.");
      return 1;
    }
    catch (InvalidOperationException)
    {
      await erro.WriteLineAsync($"Usuário '{usuario}' não existe.");
      return 1;
    }
    catch (Exception e)
    {
      // Só o tipo: a mensagem do driver pode trazer servidor e banco, e nunca deve trazer a conexão.
      await erro.WriteLineAsync($"Falha ao gravar a senha ({e.GetType().Name}).");
      return 1;
    }

    await saida.WriteLineAsync($"Senha de '{usuario}' definida.");
    return 0;
  }

  private static async Task<int> ConferirSenhasAsync(
      string[] args, IConfiguration configuracao, TextWriter saida, TextWriter erro)
  {
    if (args.Length != 1)
    {
      await erro.WriteLineAsync("Uso: conferir-senhas");
      return 2;
    }

    var conexaoTexto = configuracao.GetConnectionString("Rastreamento");
    if (string.IsNullOrWhiteSpace(conexaoTexto))
    {
      await erro.WriteLineAsync("Conexão não configurada: defina ConnectionStrings__Rastreamento.");
      return 1;
    }

    IReadOnlyList<string> com;
    try
    {
      await using var conexao = new SqlConnection(conexaoTexto);
      await conexao.OpenAsync();
      com = await new SenhasDosUsuarios(new BCryptPasswordHasher())
          .ComSenhaDoSeedAsync(conexao, CancellationToken.None);
    }
    catch (Exception e)
    {
      await erro.WriteLineAsync($"Falha ao conferir as senhas ({e.GetType().Name}).");
      return 1;
    }

    if (com.Count == 0)
    {
      await saida.WriteLineAsync("Nenhum dos sete usuários do seed tem a senha do repositório.");
      return 0;
    }

    await saida.WriteLineAsync("Usuários com a senha pública do repositório:");
    foreach (var nome in com) await saida.WriteLineAsync($"  {nome}");
    return 1;
  }
}

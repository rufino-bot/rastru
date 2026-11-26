using Microsoft.Data.SqlClient;

namespace Rastreamento.Infrastructure.Banco;

public sealed record ResultadoDoPreparo(bool BancoNovo, IReadOnlyList<string> MigracoesAplicadas, string? Backup);

/// <summary>
/// O que o deploy roda a partir da imagem (<c>banco preparar</c>): banco que não existe é criado (de forma
/// atômica), banco que existe é migrado, e nos dois casos o login do ambiente é garantido no fim.
/// </summary>
public sealed class PreparadorDeBanco(CriadorDeBanco criador, MigradorDeBanco migrador, LoginDoAmbiente login)
{
  public async Task<ResultadoDoPreparo> PrepararAsync(
      OpcoesDoBanco opcoes, IReadOnlyDictionary<string, string> senhas, CancellationToken ct)
  {
    // Nada de criar banco para descobrir, no fim, que faltava o login.
    if (string.IsNullOrWhiteSpace(opcoes.ConexaoAdministrativa))
      throw new InvalidOperationException("Banco:ConexaoAdministrativa não configurada.");
    if (string.IsNullOrWhiteSpace(opcoes.Nome))
      throw new InvalidOperationException("Banco:Nome não configurado.");
    if (string.IsNullOrWhiteSpace(opcoes.Login) || string.IsNullOrEmpty(opcoes.SenhaDoLogin))
      throw new InvalidOperationException("Banco:Login e Banco:SenhaDoLogin não configurados.");

    var scripts = new ScriptsDoBanco(opcoes.RaizDosScripts);

    bool existe;
    await using (var master = new SqlConnection(opcoes.ConexaoAdministrativa))
    {
      await master.OpenAsync(ct);
      await using var cmd = new SqlCommand("SELECT DB_ID(@nome)", master);
      cmd.Parameters.AddWithValue("@nome", opcoes.Nome);
      existe = await cmd.ExecuteScalarAsync(ct) is not (null or DBNull);
    }

    ResultadoDoPreparo resultado;
    if (existe)
    {
      var migracao = await migrador.MigrarAsync(opcoes.ConexaoAdministrativa, opcoes.Nome, scripts, ct);
      resultado = new ResultadoDoPreparo(false, migracao.Aplicadas, migracao.Backup);
    }
    else
    {
      await criador.CriarAsync(opcoes.ConexaoAdministrativa, opcoes.Nome, opcoes.Vitrine, senhas, scripts, ct);
      resultado = new ResultadoDoPreparo(true, [], null);
    }

    await login.GarantirAsync(opcoes.ConexaoAdministrativa, opcoes.Nome, opcoes.Login, opcoes.SenhaDoLogin, ct);
    return resultado;
  }
}

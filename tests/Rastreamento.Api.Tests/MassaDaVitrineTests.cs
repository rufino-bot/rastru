using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Rastreamento.Infrastructure.Banco;
using Rastreamento.Infrastructure.Security;
using Rastreamento.Infrastructure.Tests.Banco;

namespace Rastreamento.Api.Tests;

/// <summary>
/// A massa de vitrine da HML (D11 da spec do CI/CD): o banco novo criado com <c>vitrine = true</c> ganha o
/// <c>db/seed-demo.sql</c> e o <c>db/massa-vitrine.sql</c>, com os oito Pedidos nos estágios que as telas mostram. O
/// banco é próprio da classe (<see cref="BancoDaVitrine"/>): o compartilhado não ganha nada, e a suíte continua
/// determinística.
/// </summary>
public class MassaDaVitrineTests(MassaDaVitrineTests.BancoDaVitrine vitrine) : IClassFixture<MassaDaVitrineTests.BancoDaVitrine>
{
  private static readonly string[] OitoPedidos =
      ["PED-1041", "PED-1042", "PED-1043", "PED-1044", "PED-1045", "PED-1046", "PED-1047", "PED-1048"];

  [Fact]
  public async Task A_vitrine_tem_os_oito_pedidos()
  {
    var numeros = await vitrine.ListarAsync<string>(
        "SELECT Numero FROM dbo.Pedido WHERE Tipo = 'Fabricacao' ORDER BY Numero");

    Assert.Equal(OitoPedidos, numeros);
  }

  [Fact]
  public async Task Todo_no_fecha_a_conservacao()
  {
    // Para cada nó e cada posição (posição, Setor, passo): o que entrou menos o que saiu, e o AIniciar começa na
    // quantidade do nó. Nenhum saldo negativo (regra 9, com o perdido como termo), e a soma das posições é a
    // quantidade cadastrada.
    const string saldos = """
        WITH Lados AS (
            SELECT EstruturaItemId, DestinoPosicao AS Posicao, DestinoSetorId AS SetorId, DestinoOrdem AS Ordem, Quantidade
            FROM dbo.Movimentacao
            UNION ALL
            SELECT EstruturaItemId, OrigemPosicao, OrigemSetorId, OrigemOrdem, -Quantidade
            FROM dbo.Movimentacao
            UNION ALL
            SELECT Id, 'AIniciar', NULL, NULL, Quantidade FROM dbo.EstruturaItem
        )
        SELECT EstruturaItemId, Posicao, SetorId, Ordem, SUM(Quantidade) AS Saldo
        INTO #Saldo
        FROM Lados
        GROUP BY EstruturaItemId, Posicao, SetorId, Ordem;
        """;
    await using var conexao = await vitrine.AbrirAsync();
    await using (var cmd = new SqlCommand(saldos, conexao)) await cmd.ExecuteNonQueryAsync();

    Assert.Equal(0, await Escalar<int>(conexao, "SELECT COUNT(*) FROM #Saldo WHERE Saldo < 0"));
    Assert.Equal(0, await Escalar<int>(conexao, """
        SELECT COUNT(*) FROM dbo.EstruturaItem e
        WHERE e.Quantidade <> (SELECT SUM(s.Saldo) FROM #Saldo s WHERE s.EstruturaItemId = e.Id)
        """));
    Assert.True(await Escalar<int>(conexao, "SELECT COUNT(*) FROM dbo.Movimentacao") > 0);
  }

  [Theory]
  [InlineData("PED-1041")]
  [InlineData("PED-1042")]
  [InlineData("PED-1043")]
  [InlineData("PED-1044")]
  [InlineData("PED-1045")]
  [InlineData("PED-1046")]
  [InlineData("PED-1047")]
  [InlineData("PED-1048")]
  public async Task Cada_estagio_existe(string numero)
  {
    await using var conexao = await vitrine.AbrirAsync();
    var id = await Escalar<int>(conexao, $"SELECT Id FROM dbo.Pedido WHERE Numero = '{numero}'");
    var status = await Escalar<string>(conexao, $"SELECT Status FROM dbo.Pedido WHERE Id = {id}");

    // Saldo do Pedido por posição e Setor, a partir do livro.
    // (Tabela derivada, e não CTE, porque entra como subconsulta nas asserções.)
    var saldoDoPedido = $"""
        SELECT l.Posicao, l.SetorId, SUM(l.Quantidade) AS Saldo
        FROM (
            SELECT m.EstruturaItemId, m.DestinoPosicao AS Posicao, m.DestinoSetorId AS SetorId, m.Quantidade
            FROM dbo.Movimentacao m
            UNION ALL
            SELECT m.EstruturaItemId, m.OrigemPosicao, m.OrigemSetorId, -m.Quantidade
            FROM dbo.Movimentacao m
        ) l
        JOIN dbo.EstruturaItem e ON e.Id = l.EstruturaItemId
        JOIN dbo.Agrupamento a ON a.Id = e.AgrupamentoId
        WHERE a.PedidoId = {id}
        GROUP BY l.Posicao, l.SetorId
        HAVING SUM(l.Quantidade) > 0
        """;

    switch (numero)
    {
      case "PED-1041":
        Assert.Contains(status, new[] { "Aberto", "EmProducao", "AguardandoExpedicao" });
        Assert.Equal(1, await Escalar<int>(conexao,
            $"SELECT CASE WHEN DataEntrega < CAST(DATEADD(HOUR, -3, SYSUTCDATETIME()) AS DATE) THEN 1 ELSE 0 END FROM dbo.Pedido WHERE Id = {id}"));
        break;
      case "PED-1042":
        Assert.Equal("EmProducao", status);
        Assert.Equal(1, await Escalar<int>(conexao,
            $"SELECT COUNT(*) FROM dbo.PedidoPausa WHERE PedidoId = {id} AND RetomadoEm IS NULL"));
        break;
      case "PED-1043":
        Assert.Equal("EmProducao", status);
        Assert.True(await Escalar<int>(conexao,
            $"SELECT COUNT(DISTINCT s.SetorId) FROM ({saldoDoPedido}) s WHERE s.SetorId IS NOT NULL") >= 3);
        Assert.True(await Escalar<int>(conexao,
            $"SELECT COUNT(*) FROM ({saldoDoPedido}) s WHERE s.Posicao = 'AguardandoColeta'") >= 1);
        break;
      case "PED-1044":
        Assert.Equal("EmProducao", status);
        Assert.Equal(1, await Escalar<int>(conexao,
            $"SELECT COUNT(*) FROM dbo.Agrupamento WHERE PedidoId = {id} AND Tipo = 'Kit'"));
        Assert.True(await Escalar<int>(conexao, $"""
            SELECT COUNT(*) FROM ({saldoDoPedido}) s JOIN dbo.Setor st ON st.Id = s.SetorId
            WHERE s.Posicao = 'AguardandoMontagem' AND st.Nome = N'Solda' AND st.UtilizaKit = 1
            """) >= 1);
        Assert.True(await Escalar<int>(conexao, $"""
            SELECT COUNT(*) FROM dbo.Montagem m JOIN dbo.EstruturaItem e ON e.Id = m.EstruturaItemId
            JOIN dbo.Agrupamento a ON a.Id = e.AgrupamentoId WHERE a.PedidoId = {id}
            """) >= 1);
        break;
      case "PED-1045":
        Assert.Equal("AguardandoExpedicao", status);
        foreach (var resultado in new[] { "Conforme", "NaoConforme" })
          Assert.True(await Escalar<int>(conexao, $"""
              SELECT COUNT(*) FROM dbo.RelatorioDimensional r JOIN dbo.EstruturaItem e ON e.Id = r.EstruturaItemId
              JOIN dbo.Agrupamento a ON a.Id = e.AgrupamentoId
              WHERE a.PedidoId = {id} AND r.Tipo = 'Afericao' AND r.Resultado = '{resultado}'
              """) >= 1, resultado);
        Assert.True(await Escalar<int>(conexao, $"""
            SELECT COUNT(*) FROM dbo.Movimentacao m JOIN dbo.EstruturaItem e ON e.Id = m.EstruturaItemId
            JOIN dbo.Agrupamento a ON a.Id = e.AgrupamentoId WHERE a.PedidoId = {id} AND m.Tipo = 'Expedicao'
            """) >= 1);
        break;
      case "PED-1046":
        Assert.Equal(1, await Escalar<int>(conexao,
            $"SELECT COUNT(*) FROM dbo.Perda WHERE PedidoId = {id} AND Motivo = 'MortaEmProcesso'"));
        Assert.Equal(1, await Escalar<int>(conexao, $"""
            SELECT COUNT(*) FROM dbo.Pedido r
            JOIN dbo.Perda p ON p.PedidoRetrabalhoId = r.Id AND p.PedidoId = {id}
            WHERE r.Tipo = 'Retrabalho' AND r.PedidoOrigemId = {id} AND r.MotivoRetrabalho = 'Perda'
              AND r.Numero = N'{numero}-R1'
            """));
        break;
      case "PED-1047":
        Assert.Equal("Aberto", status);
        Assert.Equal(0, await Escalar<int>(conexao, $"""
            SELECT COUNT(*) FROM dbo.Movimentacao m JOIN dbo.EstruturaItem e ON e.Id = m.EstruturaItemId
            JOIN dbo.Agrupamento a ON a.Id = e.AgrupamentoId WHERE a.PedidoId = {id}
            """));
        break;
      case "PED-1048":
        Assert.Equal("Concluido", status);
        Assert.Equal(1, await Escalar<int>(conexao,
            $"SELECT CASE WHEN DataConclusao IS NOT NULL THEN 1 ELSE 0 END FROM dbo.Pedido WHERE Id = {id}"));
        break;
    }
  }

  [Fact]
  public async Task A_api_le_a_vitrine()
  {
    var cliente = vitrine.Fabrica.CreateClient();
    cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Bearer", TokenDeTeste.Emitir(vitrine.Fabrica, "Administrador"));

    var panorama = await cliente.GetAsync("/api/panorama");
    Assert.Equal(HttpStatusCode.OK, panorama.StatusCode);

    foreach (var id in await vitrine.ListarAsync<int>("SELECT Id FROM dbo.Pedido ORDER BY Id"))
    {
      var pedido = await cliente.GetAsync($"/api/pedidos/{id}");
      Assert.True(pedido.StatusCode == HttpStatusCode.OK, $"GET /api/pedidos/{id}: {(int)pedido.StatusCode}");
      var andamento = await cliente.GetAsync($"/api/pedidos/{id}/andamento");
      Assert.True(andamento.StatusCode == HttpStatusCode.OK,
          $"GET /api/pedidos/{id}/andamento: {(int)andamento.StatusCode} {await andamento.Content.ReadAsStringAsync()}");
    }
  }

  [Fact]
  public async Task Carregar_de_novo_nao_duplica()
  {
    const string contagens = """
        SELECT CONCAT((SELECT COUNT(*) FROM dbo.Pedido), '/', (SELECT COUNT(*) FROM dbo.EstruturaItem), '/',
                      (SELECT COUNT(*) FROM dbo.Movimentacao), '/', (SELECT COUNT(*) FROM dbo.Perda), '/',
                      (SELECT COUNT(*) FROM dbo.Remessa), '/', (SELECT COUNT(*) FROM dbo.PedidoPausa))
        """;
    await using var conexao = await vitrine.AbrirAsync();
    var antes = await Escalar<string>(conexao, contagens);

    var scripts = new ScriptsDoBanco(BancoDescartavel.RaizDoRepositorio());
    await ExecutorDeSql.ExecutarAsync(
        conexao, null, await File.ReadAllTextAsync(scripts.MassaVitrine, System.Text.Encoding.UTF8), CancellationToken.None);

    Assert.Equal(antes, await Escalar<string>(conexao, contagens));
  }

  private static async Task<T> Escalar<T>(SqlConnection conexao, string sql)
  {
    await using var cmd = new SqlCommand(sql, conexao);
    return (T)(await cmd.ExecuteScalarAsync())!;
  }

  /// <summary>
  /// O banco da classe: criado pelo <see cref="CriadorDeBanco"/> com <c>vitrine = true</c>, e uma
  /// <see cref="WebApplicationFactory{TEntryPoint}"/> apontada para ele com <c>sa</c>. O <c>DisposeAsync</c> apaga o banco
  /// e o <c>_criando</c> que tenha sobrado.
  /// </summary>
  public sealed class BancoDaVitrine : IAsyncLifetime
  {
    private BancoDescartavel? _banco;
    private WebApplicationFactory<Program>? _fabrica;

    public WebApplicationFactory<Program> Fabrica => _fabrica!;

    public async Task InitializeAsync()
    {
      _banco = await BancoDescartavel.ReservarNomeAsync();
      var senhas = SenhasDoSeed.Todas.ToDictionary(s => s.Usuario, s => $"Vitrine-{s.Usuario}-{Guid.NewGuid():N}-Aa1!");
      try
      {
        await new CriadorDeBanco(new SenhasDosUsuarios(new BCryptPasswordHasher())).CriarAsync(
            _banco.ConexaoAdministrativa, _banco.Nome, vitrine: true, senhas,
            new ScriptsDoBanco(BancoDescartavel.RaizDoRepositorio()), CancellationToken.None);
      }
      catch
      {
        // Sem depender de o xUnit chamar o DisposeAsync de uma fixture que não inicializou.
        await _banco.DisposeAsync();
        _banco = null;
        throw;
      }

      var conexao = _banco.ConexaoDoBanco;
      _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
          b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
          {
            ["ConnectionStrings:Rastreamento"] = conexao,
          })));
    }

    public async Task<SqlConnection> AbrirAsync()
    {
      var conexao = new SqlConnection(_banco!.ConexaoDoBanco);
      await conexao.OpenAsync();
      return conexao;
    }

    public async Task<List<T>> ListarAsync<T>(string sql)
    {
      await using var conexao = await AbrirAsync();
      await using var cmd = new SqlCommand(sql, conexao);
      var lista = new List<T>();
      await using var leitor = await cmd.ExecuteReaderAsync();
      while (await leitor.ReadAsync()) lista.Add(leitor.GetFieldValue<T>(0));
      return lista;
    }

    public async Task DisposeAsync()
    {
      try
      {
        if (_fabrica is not null) await _fabrica.DisposeAsync();
      }
      finally
      {
        if (_banco is not null) await _banco.DisposeAsync();
      }
    }
  }
}

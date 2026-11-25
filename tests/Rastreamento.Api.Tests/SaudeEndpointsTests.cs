using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Rastreamento.Api.Tests;

/// <summary>
/// `GET /api/saude`: a rota anonima que o orquestrador e a verificacao do deploy chamam. Confere
/// o banco e responde so o status, sem detalhe de erro no corpo.
/// </summary>
public class SaudeEndpointsTests
{
  private const string ConexaoInalcancavel =
      "Server=localhost,1;Database=Rastreamento;User Id=sa;Password=x;" +
      "TrustServerCertificate=True;Connect Timeout=2";

  [Fact]
  public async Task Saude_com_banco_responde_200_sem_token()
  {
    using var fabrica = new WebApplicationFactory<Program>();
    var cliente = fabrica.CreateClient();

    var resposta = await cliente.GetAsync("/api/saude");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    using var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
    Assert.Equal("ok", corpo.RootElement.GetProperty("status").GetString());
  }

  [Fact]
  public async Task Saude_sem_banco_responde_503_sem_detalhe()
  {
    using var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["ConnectionStrings:Rastreamento"] = ConexaoInalcancavel,
        })));
    var cliente = fabrica.CreateClient();

    var resposta = await cliente.GetAsync("/api/saude");

    Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
    var texto = await resposta.Content.ReadAsStringAsync();
    using var corpo = JsonDocument.Parse(texto);
    Assert.Equal("indisponivel", corpo.RootElement.GetProperty("status").GetString());
    Assert.DoesNotContain("localhost", texto);
    Assert.DoesNotContain("SqlException", texto);
    Assert.DoesNotContain("Exception", texto);
  }
}

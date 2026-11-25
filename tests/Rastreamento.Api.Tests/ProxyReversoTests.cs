using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rastreamento.Api.Tests;

/// <summary>
/// <c>ForwardedHeaders</c> restrito a rede do proxy (secao <c>ProxyReverso</c>). Cada teste usa o
/// limite de UMA tentativa por janela no <c>/auth/login</c>: a segunda tentativa da mesma particao
/// vira 429, e quem prova que o <c>X-Forwarded-For</c> foi (ou nao foi) honrado e a particao. O
/// <c>RemoteIpAddress</c> e fixado por um <c>IStartupFilter</c>, porque no <c>TestServer</c> ele e
/// nulo. Usuario inexistente de proposito, como em <c>RateLimitTests</c>.
/// </summary>
public class ProxyReversoTests
{
  private const string RedeDoProxy = "172.30.0.0/24";

  private static HttpClient NovoCliente(string ipRemoto, string? rede)
  {
    var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
      b.ConfigureAppConfiguration(c =>
      {
        var chaves = new Dictionary<string, string?>
        {
          ["RateLimit:PermitLimit"] = "1",
          ["RateLimit:WindowSeconds"] = "60",
        };
        if (rede is not null) chaves["ProxyReverso:Rede"] = rede;
        c.AddInMemoryCollection(chaves);
      });
      b.ConfigureServices(s => s.AddSingleton<IStartupFilter>(new FixaIpRemoto(IPAddress.Parse(ipRemoto))));
    });

    return fabrica.CreateClient(new WebApplicationFactoryClientOptions
    {
      BaseAddress = new Uri("https://localhost"),
    });
  }

  private static async Task<HttpStatusCode> LoginComoAsync(HttpClient cliente, string ipDoCliente)
  {
    using var requisicao = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
    {
      Content = JsonContent.Create(new { nomeUsuario = $"ninguem-{Guid.NewGuid():N}", senha = "errada" }),
    };
    requisicao.Headers.Add("X-Forwarded-For", ipDoCliente);
    return (await cliente.SendAsync(requisicao)).StatusCode;
  }

  [Fact]
  public async Task Clientes_atras_do_proxy_tem_particoes_proprias()
  {
    var cliente = NovoCliente("172.30.0.5", RedeDoProxy);

    Assert.Equal(HttpStatusCode.Unauthorized, await LoginComoAsync(cliente, "203.0.113.7"));
    Assert.Equal(HttpStatusCode.Unauthorized, await LoginComoAsync(cliente, "203.0.113.8"));
    Assert.Equal(HttpStatusCode.TooManyRequests, await LoginComoAsync(cliente, "203.0.113.7"));
  }

  [Fact]
  public async Task Cabecalho_de_fora_da_rede_e_ignorado()
  {
    var cliente = NovoCliente("198.51.100.9", RedeDoProxy);

    Assert.Equal(HttpStatusCode.Unauthorized, await LoginComoAsync(cliente, "203.0.113.7"));
    Assert.Equal(HttpStatusCode.TooManyRequests, await LoginComoAsync(cliente, "203.0.113.8"));
  }

  [Fact]
  public async Task Sem_rede_configurada_o_cabecalho_e_ignorado()
  {
    var cliente = NovoCliente("172.30.0.5", rede: null);

    Assert.Equal(HttpStatusCode.Unauthorized, await LoginComoAsync(cliente, "203.0.113.7"));
    Assert.Equal(HttpStatusCode.TooManyRequests, await LoginComoAsync(cliente, "203.0.113.8"));
  }

  private sealed class FixaIpRemoto(IPAddress ip) : IStartupFilter
  {
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> proximoFiltro) =>
        app =>
        {
          app.Use(async (contexto, proximo) =>
          {
            contexto.Connection.RemoteIpAddress = ip;
            await proximo();
          });

          proximoFiltro(app);
        };
  }
}

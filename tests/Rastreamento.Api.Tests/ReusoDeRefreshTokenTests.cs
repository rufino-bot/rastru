using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Reuso de refresh token ponta a ponta, contra o SQL Server real (docker compose up -d + seed).
/// </summary>
public class ReusoDeRefreshTokenTests : IClassFixture<WebApplicationFactory<Program>>
{
  private const string NomeDoCookie = "refreshToken";

  private readonly WebApplicationFactory<Program> _factory;

  public ReusoDeRefreshTokenTests(WebApplicationFactory<Program> factory) => _factory = factory;

  [Fact]
  public async Task Reapresentar_refresh_ja_rotacionado_derruba_todas_as_sessoes_do_usuario()
  {
    await using var usuario = await UsuarioDeTeste.CriarAsync(_factory.Services, "reuso");

    // O legitimo faz login (token A) e renova uma vez (token B).
    var cliente = NovoCliente();
    var login = await cliente.PostAsJsonAsync("/api/auth/login",
        new { nomeUsuario = usuario.NomeUsuario, senha = UsuarioDeTeste.Senha });
    Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    var tokenA = ValorDoRefresh(login);

    var renovacao = await ComRefresh(tokenA).PostAsync("/api/auth/refresh", null);
    Assert.Equal(HttpStatusCode.OK, renovacao.StatusCode);
    var tokenB = ValorDoRefresh(renovacao);

    // Alguem reapresenta o A (ja rotacionado): sinal de que o refresh vazou.
    var replay = await ComRefresh(tokenA).PostAsync("/api/auth/refresh", null);
    Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

    // O ponto da defesa: o B — que estava valido ate agora — cai junto.
    var aposQueima = await ComRefresh(tokenB).PostAsync("/api/auth/refresh", null);
    Assert.Equal(HttpStatusCode.Unauthorized, aposQueima.StatusCode);

    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var tokens = await db.RefreshTokens.AsNoTracking()
        .Where(t => t.UsuarioId == usuario.Id).ToListAsync();
    Assert.NotEmpty(tokens);
    Assert.All(tokens, t => Assert.NotNull(t.RevogadoEm));
  }

  [Fact]
  public async Task Reuso_responde_igual_a_token_desconhecido()
  {
    // Sem oraculo: a queima e efeito colateral so no banco. Quem chama nao pode perceber que
    // acertou um token que existiu — isso confirmaria ao ladrao que o token era real.
    await using var usuario = await UsuarioDeTeste.CriarAsync(_factory.Services, "reuso");

    var cliente = NovoCliente();
    var login = await cliente.PostAsJsonAsync("/api/auth/login",
        new { nomeUsuario = usuario.NomeUsuario, senha = UsuarioDeTeste.Senha });
    var tokenA = ValorDoRefresh(login);
    await ComRefresh(tokenA).PostAsync("/api/auth/refresh", null);

    var reuso = await ComRefresh(tokenA).PostAsync("/api/auth/refresh", null);
    var desconhecido = await ComRefresh("token-que-nunca-existiu").PostAsync("/api/auth/refresh", null);

    Assert.Equal(desconhecido.StatusCode, reuso.StatusCode);
    Assert.Equal(await desconhecido.Content.ReadAsStringAsync(),
                 await reuso.Content.ReadAsStringAsync());
  }

  // BaseAddress https: o CookieContainer do HttpClient so reenvia cookies Secure em https.
  private HttpClient NovoCliente() =>
      _factory.CreateClient(new WebApplicationFactoryClientOptions
      {
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost"),
      });

  /// <summary>Cliente sem cookie container, com um refresh token especifico no header.</summary>
  private HttpClient ComRefresh(string refreshPlano)
  {
    var cliente = _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
      HandleCookies = false,
      BaseAddress = new Uri("https://localhost"),
    });
    cliente.DefaultRequestHeaders.Add("Cookie", $"{NomeDoCookie}={refreshPlano}");
    return cliente;
  }

  private static string ValorDoRefresh(HttpResponseMessage resposta)
  {
    var cookie = resposta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith($"{NomeDoCookie}="));
    return cookie[(NomeDoCookie.Length + 1)..cookie.IndexOf(';')];
  }
}

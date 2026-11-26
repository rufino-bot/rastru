using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Guardas do prefixo <c>/api</c> (a recusa de caminho fora de <c>/api</c> + o
/// <c>UsePathBase</c>, em Program.cs). Vivem aqui, e nao numa classe propria, porque o
/// <c>DisposeAsync</c> de
/// <see cref="AuthEndpointsTests"/> ja limpa os <c>RefreshToken</c> de <c>admin</c> — uma classe
/// nova deixaria linha orfa no banco.
///
/// Por que estes testes existem: o resto da suite exercita URLs sob <c>/api</c> e ficaria verde
/// mesmo que o caminho nu voltasse a responder. Sem estas guardas, a colisao com as rotas do SPA
/// poderia ser reaberta sem que nada acusasse.
/// </summary>
public partial class AuthEndpointsTests
{
  /// <summary>
  /// O fechamento da transicao. <c>UsePathBase</c> sozinho NAO ramifica — ele tira o prefixo
  /// quando existe e deixa passar quando nao existe, entao ate aqui a API respondia nos DOIS
  /// caminhos e a colisao com as rotas do SPA (/setores, /pedidos, /pedidos/:id) continuava de pe.
  /// 404, e nao 401, e o discriminador: 401 significaria que a rota casou e so faltou token.
  /// </summary>
  [Theory]
  [InlineData("/setores")]
  [InlineData("/auth/login")]
  [InlineData("/me")]
  public async Task Caminho_sem_o_prefixo_nao_responde(string caminho)
  {
    var resposta = await _factory.CreateClient().GetAsync(caminho);

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  /// <summary>
  /// O POST nu, que o <c>[Theory]</c> acima nao prova: la o caso <c>/auth/login</c> vai por GET,
  /// e GET nem casaria a rota. O verbo perigoso e este — e o POST que queimaria BCrypt e gravaria
  /// cookie de refresh sob <c>Path=/auth</c>, fora do alcance de <c>/api/auth/refresh</c>.
  /// </summary>
  [Fact]
  public async Task Post_de_login_sem_o_prefixo_nao_responde()
  {
    var resposta = await _factory.CreateClient().PostAsJsonAsync("/auth/login", Credenciais);

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  /// <summary>
  /// O caso IIS: sob virtual directory / sub-application (deploy on-premise em
  /// <c>https://servidor/rastreamento</c>) toda requisicao chega com <c>PathBase</c> ja
  /// preenchido. Um predicado escrito sobre <c>PathBase.HasValue</c> passaria a liberar o caminho
  /// nu justamente ali, e o <c>TestServer</c> nunca acusaria — nele o <c>PathBase</c> do host e
  /// sempre vazio. O <c>IStartupFilter</c> abaixo injeta o <c>PathBase</c> da sub-aplicacao antes
  /// do pipeline da aplicacao para reproduzir esse cenario.
  /// </summary>
  [Fact]
  public async Task Caminho_sem_o_prefixo_nao_responde_sob_sub_aplicacao()
  {
    var fabrica = _factory.WithWebHostBuilder(host => host.ConfigureServices(
        servicos => servicos.AddSingleton<IStartupFilter, PathBaseDeSubAplicacao>()));

    var resposta = await fabrica.CreateClient().GetAsync("/setores");

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  /// <summary>
  /// Simula o que o modulo do IIS faz numa sub-application: seta o <c>PathBase</c> da requisicao
  /// antes de qualquer middleware da aplicacao, sem mexer no <c>Path</c>.
  /// </summary>
  private sealed class PathBaseDeSubAplicacao : IStartupFilter
  {
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> proximoFiltro) =>
        app =>
        {
          app.Use(async (contexto, proximo) =>
          {
            contexto.Request.PathBase = "/subapp";
            await proximo();
          });

          proximoFiltro(app);
        };
  }

  /// <summary>
  /// Casing. <c>PathString.StartsWithSegments</c> e <c>OrdinalIgnoreCase</c> por padrao, entao sem
  /// <c>StringComparison.Ordinal</c> explicito no predicado <c>/API/setores</c> tambem casaria a
  /// guarda e a API responderia. Isso nao reabre a colisao com o SPA (que usa caminhos nus, sem
  /// prefixo algum), mas o <c>UsePathBaseMiddleware</c> grava em <c>PathBase</c> o segmento como
  /// chegou na requisicao — entao um login em <c>/API/auth/login</c> gravaria o cookie de refresh
  /// com <c>Path=/API/auth</c>, e matching de <c>Path</c> de cookie e case-SENSITIVE (RFC 6265
  /// Sec5.1.4): esse cookie nunca voltaria para <c>/api/auth/refresh</c> em minuscula, e a sessao
  /// morreria no primeiro refresh, sem erro nenhum na hora do login. Por isso maiuscula e
  /// recusada (404 alto, imediato) em vez de aceita: e preferivel a sessao que morre calada.
  ///
  /// Prova por mutacao (fix pass, 2026-08-04): revertendo o predicado para a sobrecarga sem
  /// <c>StringComparison</c> (equivalente a <c>OrdinalIgnoreCase</c>), este teste passou a falhar
  /// recebendo <c>401 Unauthorized</c> em vez do <c>404</c> esperado — a guarda deixou
  /// <c>/API/setores</c> passar e a rota casou normalmente, so faltando token. Restaurado o
  /// <c>StringComparison.Ordinal</c> em seguida.
  /// </summary>
  [Fact]
  public async Task Caminho_com_prefixo_em_maiuscula_nao_responde()
  {
    var resposta = await _factory.CreateClient().GetAsync("/API/setores");

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  /// <summary>
  /// Testemunho local e rapido de que 404 nao e a resposta universal: sem ele, o par 404/401 que
  /// este arquivo documenta ficaria so metade, e um 404 vindo de rota quebrada (e nao da guarda)
  /// pareceria sucesso aqui dentro. 401, e nao 404, significa que a rota casou e so faltou token.
  /// </summary>
  [Fact]
  public async Task Caminho_sob_o_prefixo_responde()
  {
    var resposta = await _factory.CreateClient().GetAsync("/api/setores");

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }

  /// <summary>
  /// O <c>Path</c> do cookie de refresh acompanha o <c>PathBase</c>. Com prefixo unico isso da
  /// sempre <c>/api/auth</c>, mas a derivacao continua sendo o que impede o cookie de ser gravado
  /// fora do alcance do <c>/api/auth/refresh</c> — trocar por <c>"/auth"</c> literal quebra o
  /// teste seguinte.
  /// </summary>
  [Fact]
  public async Task Cookie_de_refresh_acompanha_o_prefixo()
  {
    var resposta = await NovoCliente().PostAsJsonAsync("/api/auth/login", Credenciais);

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    Assert.Contains("path=/api/auth", CookieDeRefresh(resposta), StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// A prova de ponta a ponta, e a que fecha o buraco de verdade: o teste acima le o header, este
  /// exercita o navegador. O <c>CookieContainer</c> do HttpClient respeita <c>Path</c>, entao se o
  /// cookie fosse gravado sob o prefixo errado ele nao seria reenviado e o refresh viria 401.
  /// </summary>
  [Fact]
  public async Task Refresh_sob_o_prefixo_novo_recebe_o_cookie_de_volta()
  {
    var cliente = NovoCliente();
    var login = await cliente.PostAsJsonAsync("/api/auth/login", Credenciais);
    Assert.Equal(HttpStatusCode.OK, login.StatusCode);

    var refresh = await cliente.PostAsync("/api/auth/refresh", content: null);

    Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
  }

  // ---- Modo SPA: a API serve o build do Vite de wwwroot quando ele tem index.html ----

  /// <summary>
  /// Fabrica com um <c>wwwroot</c> temporario (index.html, um asset com hash e um favicon), o que
  /// liga o modo SPA. Descartar a fabrica apaga a pasta.
  /// </summary>
  private sealed class FabricaComSpaDescartavel : IDisposable
  {
    private readonly string _pasta =
        Path.Combine(Path.GetTempPath(), "rastru-spa-" + Guid.NewGuid().ToString("N"));

    public WebApplicationFactory<Program> Fabrica { get; }

    public FabricaComSpaDescartavel(WebApplicationFactory<Program> baseDaFabrica)
    {
      Directory.CreateDirectory(Path.Combine(_pasta, "assets"));
      File.WriteAllText(Path.Combine(_pasta, "index.html"),
          "<div id=\"root\"></div><!--spa-de-teste-->");
      File.WriteAllText(Path.Combine(_pasta, "assets", "index-abc123.js"), "console.log(1);");
      File.WriteAllText(Path.Combine(_pasta, "favicon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
      Fabrica = baseDaFabrica.WithWebHostBuilder(host => host.UseWebRoot(_pasta));
    }

    public void Dispose()
    {
      Fabrica.Dispose();
      try { Directory.Delete(_pasta, recursive: true); } catch (IOException) { }
    }
  }

  [Fact]
  public async Task Spa_GET_da_raiz_devolve_o_index_sem_cache()
  {
    using var spa = new FabricaComSpaDescartavel(_factory);

    var resposta = await spa.Fabrica.CreateClient().GetAsync("/");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    Assert.Equal("text/html", resposta.Content.Headers.ContentType?.MediaType);
    Assert.Contains("spa-de-teste", await resposta.Content.ReadAsStringAsync());
    Assert.Contains("no-cache", resposta.Headers.CacheControl?.ToString());
  }

  /// <summary>O F5 numa rota do SPA: sem extensao e sem arquivo, vira o index.</summary>
  [Theory]
  [InlineData("/pedidos")]
  [InlineData("/pedidos/5")]
  [InlineData("/setores")]
  public async Task Spa_rota_do_spa_devolve_o_index(string caminho)
  {
    using var spa = new FabricaComSpaDescartavel(_factory);

    var resposta = await spa.Fabrica.CreateClient().GetAsync(caminho);

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    Assert.Contains("spa-de-teste", await resposta.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task Spa_HEAD_da_rota_responde_200()
  {
    using var spa = new FabricaComSpaDescartavel(_factory);

    var resposta = await spa.Fabrica.CreateClient()
        .SendAsync(new HttpRequestMessage(HttpMethod.Head, "/pedidos"));

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
  }

  [Fact]
  public async Task Spa_asset_com_hash_tem_cache_longo()
  {
    using var spa = new FabricaComSpaDescartavel(_factory);

    var resposta = await spa.Fabrica.CreateClient().GetAsync("/assets/index-abc123.js");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var cache = resposta.Headers.CacheControl?.ToString();
    Assert.Contains("max-age=31536000", cache);
    Assert.Contains("immutable", cache);
  }

  [Fact]
  public async Task Spa_arquivo_fora_de_assets_nao_tem_cache_longo()
  {
    using var spa = new FabricaComSpaDescartavel(_factory);

    var resposta = await spa.Fabrica.CreateClient().GetAsync("/favicon.svg");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    Assert.Contains("no-cache", resposta.Headers.CacheControl?.ToString());
  }

  /// <summary>
  /// O cliente aberto antes do deploy pede um asset que nao existe mais. Devolver o index com 200
  /// entregaria HTML como script e a tela ficaria branca sem erro util; 404 e o que aparece.
  /// </summary>
  [Fact]
  public async Task Spa_asset_inexistente_responde_404()
  {
    using var spa = new FabricaComSpaDescartavel(_factory);

    var resposta = await spa.Fabrica.CreateClient().GetAsync("/assets/index-velho.js");

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  [Fact]
  public async Task Spa_prefixo_em_maiuscula_continua_404()
  {
    using var spa = new FabricaComSpaDescartavel(_factory);
    var cliente = spa.Fabrica.CreateClient();

    Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync("/API/setores")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/api/setores")).StatusCode);
  }

  [Theory]
  [InlineData("/pedidos")]
  [InlineData("/auth/login")]
  public async Task Spa_POST_fora_do_api_responde_404(string caminho)
  {
    using var spa = new FabricaComSpaDescartavel(_factory);

    var resposta = await spa.Fabrica.CreateClient().PostAsync(caminho, content: null);

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  /// <summary>O modo so-API fica como era: sem index.html no wwwroot, a raiz e 404.</summary>
  [Fact]
  public async Task Sem_wwwroot_a_raiz_responde_404()
  {
    var resposta = await _factory.CreateClient().GetAsync("/");

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }
}

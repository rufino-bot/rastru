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
}

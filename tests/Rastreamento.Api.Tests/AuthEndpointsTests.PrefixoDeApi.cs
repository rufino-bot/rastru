using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Guardas do prefixo <c>/api</c> (<c>UsePathBase</c> + a recusa de <c>PathBase</c> vazio, em
/// Program.cs). Vivem aqui, e nao numa classe propria, porque o <c>DisposeAsync</c> de
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
  /// O par do teste acima: sem ele, apagar o <c>UsePathBase</c> inteiro deixaria a suite verde
  /// (tudo 404) e o 404 pareceria sucesso.
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

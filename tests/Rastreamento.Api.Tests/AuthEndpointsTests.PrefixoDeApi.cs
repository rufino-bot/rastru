using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Guardas do prefixo <c>/api</c> (<c>UsePathBase</c> em Program.cs). Vivem aqui, e nao numa classe
/// propria, porque o <c>DisposeAsync</c> de <see cref="AuthEndpointsTests"/> ja limpa os
/// <c>RefreshToken</c> de <c>admin</c> — uma classe nova deixaria linha orfa no banco.
///
/// Por que estes testes existem: o resto da suite de endpoints bate nos caminhos NUS
/// (<c>/setores</c>, <c>/auth/login</c>), entao apagar o <c>UsePathBase</c> deixaria os 254 verdes.
/// Sem estas guardas, o prefixo seria uma linha que ninguem percebe sumir.
/// </summary>
public partial class AuthEndpointsTests
{
    /// <summary>
    /// Servico duplo, e de proposito: enquanto os testes de endpoint baterem nos caminhos nus, a API
    /// tem que responder nos dois. 401 (e nao 404) e o discriminador — prova que a ROTA casou e so
    /// faltou token; 404 significaria que o caminho nao existe.
    /// </summary>
    [Theory]
    [InlineData("/api/setores")]
    [InlineData("/setores")]
    public async Task Os_dois_prefixos_respondem_enquanto_durar_a_transicao(string caminho)
    {
        var resposta = await _factory.CreateClient().GetAsync(caminho);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    /// <summary>
    /// O <c>Path</c> do cookie de refresh acompanha o prefixo que atendeu a requisicao. Um valor
    /// fixo so serviria a UM dos dois: cookie gravado em <c>/auth</c> nao volta para
    /// <c>/api/auth/refresh</c>, e a sessao morreria no primeiro refresh — que e exatamente o
    /// sintoma "401 ao dar F5" que o prefixo veio resolver.
    /// </summary>
    [Theory]
    [InlineData("/api/auth/login", "path=/api/auth")]
    [InlineData("/auth/login", "path=/auth")]
    public async Task Cookie_de_refresh_acompanha_o_prefixo_que_atendeu(string rota, string pathEsperado)
    {
        var resposta = await NovoCliente().PostAsJsonAsync(rota, Credenciais);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Contains(pathEsperado, CookieDeRefresh(resposta), StringComparison.OrdinalIgnoreCase);
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

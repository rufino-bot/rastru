using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Rate limit do <c>/auth/login</c>. Fabrica propria com limite pequeno: no TestServer o
/// <c>RemoteIpAddress</c> e null, entao todas as requisicoes compartilham a mesma particao — se
/// este teste usasse a fabrica compartilhada, apertaria o limite para os demais testes.
/// As tentativas usam um usuario inexistente de proposito: assim o teste nao mexe no contador de
/// lockout de nenhuma conta real.
/// </summary>
public class RateLimitTests
{
    private const int Limite = 3;
    private const int JanelaSegundos = 60;

    private static WebApplicationFactory<Program> NovaFabrica() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimit:PermitLimit"] = Limite.ToString(),
                ["RateLimit:WindowSeconds"] = JanelaSegundos.ToString(),
            })));

    private static HttpClient NovoCliente(WebApplicationFactory<Program> fabrica) =>
        fabrica.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

    private static Task<HttpResponseMessage> TentarLoginAsync(HttpClient cliente) =>
        cliente.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = $"ninguem-{Guid.NewGuid():N}", senha = "errada" });

    [Fact]
    public async Task Login_alem_do_limite_responde_429_com_Retry_After()
    {
        using var fabrica = NovaFabrica();
        var cliente = NovoCliente(fabrica);

        for (var i = 0; i < Limite; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await TentarLoginAsync(cliente)).StatusCode);

        var barrada = await TentarLoginAsync(cliente);

        Assert.Equal(HttpStatusCode.TooManyRequests, barrada.StatusCode);
        // Sem Retry-After o cliente so pode adivinhar quando voltar — e adivinhar em loop.
        Assert.True(barrada.Headers.RetryAfter is not null,
            "resposta 429 deveria trazer o header Retry-After");
        // Nao basta existir: um valor fora da janela configurada (0, negativo ou maior que
        // JanelaSegundos) orientaria o cliente a esperar tempo demais ou de menos.
        var segundosParaEsperar = barrada.Headers.RetryAfter!.Delta!.Value.TotalSeconds;
        Assert.True(segundosParaEsperar > 0 && segundosParaEsperar <= JanelaSegundos,
            $"Retry-After deveria estar em (0, {JanelaSegundos}], veio {segundosParaEsperar}");
    }

    [Fact]
    public async Task Refresh_nao_e_limitado()
    {
        // Escopo deliberado: /auth/refresh e legitimo e frequente (a cada ~15 min por usuario,
        // mais retries), e o refresh token e opaco de 256 bits — forca bruta nele e inviavel.
        // Throttlar refresh puniria o operador em wifi ruim sem fechar nenhum ataque real.
        using var fabrica = NovaFabrica();
        var cliente = NovoCliente(fabrica);

        for (var i = 0; i < Limite + 3; i++)
        {
            var resposta = await cliente.PostAsync("/auth/refresh", null);
            Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Lockout ponta a ponta, contra o SQL Server real (docker compose up -d + seed). Usa uma fabrica
/// propria com <c>MaxFalhas=3</c> — menos requisicoes que o default — e um usuario descartavel:
/// trancar o `admin` do seed por 15 minutos quebraria todo o resto da suite.
/// </summary>
public class LockoutTests
{
    private static WebApplicationFactory<Program> NovaFabrica() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lockout:MaxFalhas"] = "3",
                ["Lockout:DuracaoMinutos"] = "15",
            })));

    [Fact]
    public async Task Tres_senhas_erradas_trancam_a_conta_mesmo_para_a_senha_certa()
    {
        using var fabrica = NovaFabrica();
        await using var usuario = await UsuarioDeTeste.CriarAsync(fabrica.Services, "lockout");
        var cliente = NovoCliente(fabrica);

        for (var i = 0; i < 3; i++)
        {
            var errada = await cliente.PostAsJsonAsync("/auth/login",
                new { nomeUsuario = usuario.NomeUsuario, senha = "errada" });
            Assert.Equal(HttpStatusCode.Unauthorized, errada.StatusCode);
        }

        var comSenhaCerta = await cliente.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = usuario.NomeUsuario, senha = UsuarioDeTeste.Senha });

        Assert.Equal(HttpStatusCode.Unauthorized, comSenhaCerta.StatusCode);
        Assert.DoesNotContain("Set-Cookie", comSenhaCerta.Headers.Select(h => h.Key));

        var linha = await ComBancoAsync(fabrica, db =>
            db.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuario.Id));
        Assert.NotNull(linha.BloqueadoAte);
        Assert.True(linha.BloqueadoAte > DateTime.UtcNow);
        Assert.Equal(0, linha.FalhasConsecutivas);
    }

    [Fact]
    public async Task Conta_trancada_responde_igual_a_senha_errada()
    {
        // Sem oraculo: o atacante nao pode distinguir "tranquei a conta" de "errei a senha" —
        // saber que trancou ja confirma que a conta existe.
        using var fabrica = NovaFabrica();
        await using var usuario = await UsuarioDeTeste.CriarAsync(fabrica.Services, "lockout");
        var cliente = NovoCliente(fabrica);

        for (var i = 0; i < 3; i++)
            await cliente.PostAsJsonAsync("/auth/login",
                new { nomeUsuario = usuario.NomeUsuario, senha = "errada" });

        var trancada = await cliente.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = usuario.NomeUsuario, senha = UsuarioDeTeste.Senha });
        var inexistente = await cliente.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = $"ninguem-{Guid.NewGuid():N}", senha = "errada" });

        Assert.Equal(inexistente.StatusCode, trancada.StatusCode);
        Assert.Equal(await inexistente.Content.ReadAsStringAsync(),
                     await trancada.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Trava_expirada_libera_o_login_e_limpa_o_estado()
    {
        using var fabrica = NovaFabrica();
        await using var usuario = await UsuarioDeTeste.CriarAsync(fabrica.Services, "lockout");
        var cliente = NovoCliente(fabrica);

        for (var i = 0; i < 3; i++)
            await cliente.PostAsJsonAsync("/auth/login",
                new { nomeUsuario = usuario.NomeUsuario, senha = "errada" });

        // Empurra a trava para o passado em vez de esperar 15 minutos: o que se quer exercitar e
        // a comparacao com o relogio, nao a passagem real do tempo.
        await ComBancoAsync(fabrica, async db =>
        {
            var linha = await db.Usuarios.SingleAsync(u => u.Id == usuario.Id);
            linha.BloqueadoAte = DateTime.UtcNow.AddMinutes(-1);
            return await db.SaveChangesAsync();
        });

        var resposta = await cliente.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = usuario.NomeUsuario, senha = UsuarioDeTeste.Senha });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        var depois = await ComBancoAsync(fabrica, db =>
            db.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuario.Id));
        Assert.Null(depois.BloqueadoAte);
        Assert.Equal(0, depois.FalhasConsecutivas);
    }

    // BaseAddress https: o CookieContainer do HttpClient so reenvia cookies Secure em https.
    private static HttpClient NovoCliente(WebApplicationFactory<Program> fabrica) =>
        fabrica.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });

    private static async Task<T> ComBancoAsync<T>(
        WebApplicationFactory<Program> fabrica, Func<RastreamentoDbContext, Task<T>> consulta)
    {
        using var escopo = fabrica.Services.CreateScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>());
    }
}

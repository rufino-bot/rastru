using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;
using Rastreamento.Infrastructure.Security;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Testes de ponta a ponta dos endpoints de autenticacao. Sobem a API em memoria contra o
/// SQL Server real da Task 2 (docker compose up -d) com o seed aplicado (admin / Admin@123).
/// Cada teste apaga os RefreshToken que criou (ver <see cref="DisposeAsync"/>).
/// </summary>
public class AuthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string NomeDoCookie = "refreshToken";
    private static readonly object Credenciais = new { nomeUsuario = "admin", senha = "Admin@123" };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly Sha256TokenHasher _hasher = new();
    private int _ultimoIdAntesDoTeste;

    public AuthEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    public async Task InitializeAsync() =>
        _ultimoIdAntesDoTeste = await ComBancoAsync(db => db.RefreshTokens.MaxAsync(t => (int?)t.Id)) ?? 0;

    public async Task DisposeAsync()
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
        var criados = await db.RefreshTokens.Where(t => t.Id > _ultimoIdAntesDoTeste).ToListAsync();
        db.RefreshTokens.RemoveRange(criados);
        await db.SaveChangesAsync();
    }

    // ----- Login -------------------------------------------------------------------------

    [Fact]
    public async Task Login_valido_retorna_200_e_cookie_de_refresh_protegido()
    {
        var cliente = NovoCliente();

        var resposta = await cliente.PostAsJsonAsync("/auth/login", Credenciais);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var cookie = CookieDeRefresh(resposta);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_nao_devolve_o_refresh_token_no_corpo()
    {
        var cliente = NovoCliente();

        var resposta = await cliente.PostAsJsonAsync("/auth/login", Credenciais);

        var refreshPlano = ValorDoRefresh(resposta);
        var corpo = await resposta.Content.ReadAsStringAsync();
        Assert.DoesNotContain(refreshPlano, corpo);
        Assert.False(string.IsNullOrWhiteSpace(Campo(corpo, "accessToken").GetString()));
    }

    [Fact]
    public async Task Login_devolve_a_expiracao_do_access_token_no_fuso_de_brasilia()
    {
        var cliente = NovoCliente();

        var resposta = await cliente.PostAsJsonAsync("/auth/login", Credenciais);

        // A borda de fuso e a API: por dentro tudo e UTC, no JSON sai ISO 8601 com offset -03:00.
        var expiraEm = Campo(await resposta.Content.ReadAsStringAsync(), "accessTokenExpiraEm").GetString();
        Assert.EndsWith("-03:00", expiraEm!);
        Assert.Equal(TimeSpan.FromHours(-3), DateTimeOffset.Parse(expiraEm!).Offset);
    }

    [Fact]
    public async Task Login_com_senha_errada_retorna_401()
    {
        var cliente = NovoCliente();

        var resposta = await cliente.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = "admin", senha = "errada" });

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.DoesNotContain("Set-Cookie", resposta.Headers.Select(h => h.Key));
    }

    [Fact]
    public async Task Login_com_usuario_inexistente_responde_igual_a_senha_errada()
    {
        // Sem oraculo: quem chama nao consegue distinguir "usuario nao existe" de "senha errada".
        var cliente = NovoCliente();

        var senhaErrada = await cliente.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = "admin", senha = "errada" });
        var usuarioInexistente = await cliente.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = "ninguem", senha = "Admin@123" });

        Assert.Equal(senhaErrada.StatusCode, usuarioInexistente.StatusCode);
        Assert.Equal(await senhaErrada.Content.ReadAsStringAsync(),
            await usuarioInexistente.Content.ReadAsStringAsync());
    }

    // ----- /me ---------------------------------------------------------------------------

    [Fact]
    public async Task Me_sem_token_retorna_401()
    {
        var cliente = NovoCliente();

        var resposta = await cliente.GetAsync("/me");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Me_com_access_token_retorna_o_usuario_autenticado()
    {
        var cliente = NovoCliente();
        var login = await cliente.PostAsJsonAsync("/auth/login", Credenciais);
        var corpoLogin = await login.Content.ReadAsStringAsync();
        cliente.DefaultRequestHeaders.Authorization =
            new("Bearer", Campo(corpoLogin, "accessToken").GetString());

        var resposta = await cliente.GetAsync("/me");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await resposta.Content.ReadAsStringAsync();
        Assert.Equal("admin", Campo(corpo, "nomeUsuario").GetString());
        Assert.Equal("Administrador", Campo(corpo, "perfil").GetString());
        Assert.True(Campo(corpo, "id").GetInt32() > 0);
    }

    // ----- Refresh -----------------------------------------------------------------------

    [Fact]
    public async Task Refresh_rotaciona_o_token_e_revoga_o_antigo_no_banco()
    {
        // Este e o teste que prova a atomicidade da rotacao em producao: os fakes da camada
        // Application nao pegam um registro de DI errado (Transient faria a revogacao do token
        // antigo cair num DbContext diferente do SaveChanges e se perder em silencio).
        var cliente = NovoCliente();
        var login = await cliente.PostAsJsonAsync("/auth/login", Credenciais);
        var refreshAntigo = ValorDoRefresh(login);

        var renovacao = await cliente.PostAsync("/auth/refresh", null);

        Assert.Equal(HttpStatusCode.OK, renovacao.StatusCode);
        var refreshNovo = ValorDoRefresh(renovacao);
        Assert.NotEqual(refreshAntigo, refreshNovo);

        var hashAntigo = _hasher.Hash(refreshAntigo);
        var antigoNoBanco = await ComBancoAsync(db => db.RefreshTokens.AsNoTracking()
            .SingleAsync(t => t.TokenHash == hashAntigo));

        Assert.NotNull(antigoNoBanco.RevogadoEm);
        Assert.Equal(_hasher.Hash(refreshNovo), antigoNoBanco.SubstituidoPorTokenHash);

        var comTokenAntigo = NovoClienteComRefresh(refreshAntigo);
        var segundaRenovacao = await comTokenAntigo.PostAsync("/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, segundaRenovacao.StatusCode);
    }

    [Fact]
    public async Task Refresh_sem_cookie_retorna_401()
    {
        var cliente = NovoCliente();

        var resposta = await cliente.PostAsync("/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Refresh_com_token_desconhecido_retorna_401()
    {
        var cliente = NovoClienteComRefresh("token-que-nunca-existiu");

        var resposta = await cliente.PostAsync("/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    // ----- Logout ------------------------------------------------------------------------

    [Fact]
    public async Task Logout_revoga_o_token_no_banco_e_limpa_o_cookie()
    {
        var cliente = NovoCliente();
        var login = await cliente.PostAsJsonAsync("/auth/login", Credenciais);
        var refreshPlano = ValorDoRefresh(login);

        var resposta = await cliente.PostAsync("/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Contains("path=/auth", CookieDeRefresh(resposta), StringComparison.OrdinalIgnoreCase);

        var hash = _hasher.Hash(refreshPlano);
        var noBanco = await ComBancoAsync(db => db.RefreshTokens.AsNoTracking()
            .SingleAsync(t => t.TokenHash == hash));
        Assert.NotNull(noBanco.RevogadoEm);

        var aposLogout = await NovoClienteComRefresh(refreshPlano).PostAsync("/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, aposLogout.StatusCode);
    }

    [Fact]
    public async Task Logout_sem_cookie_retorna_204()
    {
        // Logout e idempotente por design: nunca sinaliza falha, nem vaza a existencia do token.
        var cliente = NovoCliente();

        var resposta = await cliente.PostAsync("/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
    }

    [Fact]
    public async Task Logout_com_token_desconhecido_retorna_204()
    {
        var cliente = NovoClienteComRefresh("token-que-nunca-existiu");

        var resposta = await cliente.PostAsync("/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
    }

    // ----- Apoio -------------------------------------------------------------------------

    // BaseAddress https: o CookieContainer do HttpClient so reenvia cookies Secure em https.
    private HttpClient NovoCliente() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });

    /// <summary>Cliente sem cookie container, com um refresh token especifico no header.</summary>
    private HttpClient NovoClienteComRefresh(string refreshPlano)
    {
        var cliente = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost"),
        });
        cliente.DefaultRequestHeaders.Add("Cookie", $"{NomeDoCookie}={refreshPlano}");
        return cliente;
    }

    private async Task<T> ComBancoAsync<T>(Func<RastreamentoDbContext, Task<T>> consulta)
    {
        using var escopo = _factory.Services.CreateScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>());
    }

    private static string CookieDeRefresh(HttpResponseMessage resposta) =>
        resposta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith($"{NomeDoCookie}="));

    private static string ValorDoRefresh(HttpResponseMessage resposta)
    {
        var cookie = CookieDeRefresh(resposta);
        return cookie[(NomeDoCookie.Length + 1)..cookie.IndexOf(';')];
    }

    private static JsonElement Campo(string json, string nome) =>
        JsonDocument.Parse(json).RootElement.GetProperty(nome);
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Application.Auth;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Fecha a corrida entre a queima de familia (deteccao de reuso) e uma rotacao ja em voo. Sem o
/// <c>RowVersion</c> como token de concorrencia otimista em <c>RefreshToken</c>, o SaveChanges da
/// rotacao faz um UPDATE cego (so por <c>Id</c>) que ignora a queima concorrente e insere um token
/// novo com <c>RevogadoEm = NULL</c> — vivo, e nunca coberto pela queima. Ver
/// .superpowers/sdd/design-fix-corrida-burn.md para a narrativa completa da corrida.
///
/// Reproduz de forma deterministica com dois escopos de DI (dois <see cref="RastreamentoDbContext"/>
/// distintos) em vez de threads concorrentes de verdade: escopo 1 le o token ativo (t0), escopo 2
/// queima a familia do usuario (t1), escopo 1 tenta rotacionar o token que tinha lido antes da
/// queima (t2) — exatamente a janela do defeito original.
/// </summary>
public class CorridaNaQueimaDeFamiliaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string NomeDoCookie = "refreshToken";

    private readonly WebApplicationFactory<Program> _factory;

    public CorridaNaQueimaDeFamiliaTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Rotacao_em_voo_durante_queima_concorrente_falha_e_nao_deixa_token_ativo()
    {
        await using var usuario = await UsuarioDeTeste.CriarAsync(_factory.Services, "corrida-burn");

        // Sessao real (token A), pelo caminho HTTP de producao — mesmo estado que um login deixaria.
        var cliente = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });
        var login = await cliente.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = usuario.NomeUsuario, senha = UsuarioDeTeste.Senha });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var refreshPlanoA = ValorDoRefresh(login);

        // t0 — escopo 1 le o token A, ainda ativo. Fica rastreado no DbContext deste escopo, tal
        // qual RenovarTokenUseCase.ExecutarAsync faria antes de chamar o emissor.
        using var escopo1 = _factory.Services.CreateScope();
        var hasher = escopo1.ServiceProvider.GetRequiredService<ITokenHasher>();
        var repo1 = escopo1.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
        var hash = hasher.Hash(refreshPlanoA);
        var atual = await repo1.ObterPorHashAsync(hash, CancellationToken.None);
        Assert.NotNull(atual);
        Assert.Null(atual!.RevogadoEm);

        // t1 — escopo 2 (DbContext diferente) queima toda a familia do usuario, concorrentemente:
        // exatamente o que a deteccao de reuso faz numa requisicao paralela.
        using (var escopo2 = _factory.Services.CreateScope())
        {
            var repo2 = escopo2.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
            var revogados = await repo2.RevogarTodosAtivosDoUsuarioAsync(
                usuario.Id, DateTime.UtcNow, CancellationToken.None);
            Assert.True(revogados >= 1);
        }

        // t2 — escopo 1 tenta rotacionar com o "atual" lido antes da queima. Mesmo try/catch de
        // RenovarTokenUseCase em torno da chamada ao emissor: sem o RowVersion, isto insere um
        // token novo ativo que a queima nunca cobriu (o defeito); com o RowVersion, o SaveChanges
        // inteiro reverte (o INSERT junto com o UPDATE cego) e o conflito vira falha limpa.
        var emissor1 = escopo1.ServiceProvider.GetRequiredService<IEmissorDeSessao>();
        Result<LoginResult> resultado;
        try
        {
            var novaSessao = await emissor1.RotacionarAsync(atual, CancellationToken.None);
            resultado = Result<LoginResult>.Ok(novaSessao);
        }
        catch (ConflitoDeConcorrenciaException)
        {
            resultado = Result<LoginResult>.Falha(
                "Refresh token inválido ou expirado.", TipoDeErro.NaoAutorizado);
        }

        Assert.False(resultado.Sucesso);
        Assert.Equal("Refresh token inválido ou expirado.", resultado.Erro);
        Assert.Equal(TipoDeErro.NaoAutorizado, resultado.TipoDoErro);

        // Prova sobre o estado, nao sobre o retorno: nenhuma linha ativa sobra para o usuario —
        // e isto que garante que o INSERT do token novo reverteu junto com o UPDATE.
        using var escopoVerificacao = _factory.Services.CreateScope();
        var db = escopoVerificacao.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
        var ativos = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UsuarioId == usuario.Id && t.RevogadoEm == null)
            .ToListAsync();
        Assert.Empty(ativos);
    }

    private static string ValorDoRefresh(HttpResponseMessage resposta)
    {
        var cookie = resposta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith($"{NomeDoCookie}="));
        return cookie[(NomeDoCookie.Length + 1)..cookie.IndexOf(';')];
    }
}

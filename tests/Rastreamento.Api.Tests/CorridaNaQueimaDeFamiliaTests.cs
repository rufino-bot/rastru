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
/// queima a familia do usuario (t1), escopo 1 tenta renovar com o token que tinha lido antes da
/// queima (t2) — exatamente a janela do defeito original.
///
/// Dirigido por <see cref="IRenovarTokenUseCase.ExecutarAsync"/> — o caso de uso de producao real,
/// nao uma reimplementacao do seu try/catch dentro do teste. O obstaculo e que
/// <c>ExecutarAsync</c> faz sua PROPRIA leitura do token (<c>ObterPorHashAsync</c>) em vez de
/// receber a entidade pronta; a saida e pre-carregar o token no MESMO <see cref="RastreamentoDbContext"/>
/// de escopo 1 antes da queima (t0). A resolucao de identidade do EF Core devolve, na leitura
/// interna do caso de uso (t2), a instancia JA RASTREADA daquele escopo — sem sobrescreve-la com
/// os valores atualizados do banco. E assim que o <c>RowVersion</c> obsoleto (de antes da queima)
/// acaba indo para o UPDATE em t2, e o conflito de concorrencia dispara no caminho real.
///
/// Isto NAO chega ate o endpoint HTTP <c>/auth/refresh</c>: o ASP.NET Core cria um
/// <see cref="RastreamentoDbContext"/> novo por requisicao, entao nao ha como injetar de fora um
/// contexto com a entidade ja rastreada dentro do pipeline de uma chamada HTTP real. Por isso o
/// teste para no nivel do caso de uso — que e o mesmo `IRenovarTokenUseCase` que o
/// `AuthController.Refresh` chama, sem nenhuma logica de try/catch reimplementada aqui.
/// </summary>
public class CorridaNaQueimaDeFamiliaTests : IClassFixture<WebApplicationFactory<Program>>
{
  private const string NomeDoCookie = "refreshToken";

  private readonly WebApplicationFactory<Program> _factory;

  public CorridaNaQueimaDeFamiliaTests(WebApplicationFactory<Program> factory) => _factory = factory;

  [Fact]
  public async Task Renovar_em_voo_durante_queima_concorrente_falha_e_nao_deixa_token_ativo()
  {
    await using var usuario = await UsuarioDeTeste.CriarAsync(_factory.Services, "corrida-burn");

    // Sessao real (token A), pelo caminho HTTP de producao — mesmo estado que um login deixaria.
    var cliente = _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
      HandleCookies = true,
      BaseAddress = new Uri("https://localhost"),
    });
    var login = await cliente.PostAsJsonAsync("/api/auth/login",
        new { nomeUsuario = usuario.NomeUsuario, senha = UsuarioDeTeste.Senha });
    Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    var refreshPlanoA = ValorDoRefresh(login);

    // t0 — escopo 1 le o token A, ainda ativo, com a MESMA query que
    // RenovarTokenUseCase.ExecutarAsync faz internamente (ObterPorHashAsync). Isto o deixa
    // rastreado neste DbContext de escopo — a base do truque de t2.
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

    // t2 — chama o CASO DE USO REAL no mesmo escopo 1. ExecutarAsync faz sua propria
    // ObterPorHashAsync, mas a resolucao de identidade do EF devolve a instancia "atual" (t0),
    // ja rastreada, sem reler os valores do banco: RevogadoEm ainda null, RowVersion ainda o de
    // antes da queima. Isto faz ExecutarAsync seguir o caminho feliz e delegar para
    // IEmissorDeSessao.RotacionarAsync, cujo SaveChanges leva o RowVersion obsoleto para o
    // UPDATE — o mesmo catch (ConflitoDeConcorrenciaException) de producao dentro do proprio
    // RenovarTokenUseCase e quem responde aqui, nao o teste.
    var renovar1 = escopo1.ServiceProvider.GetRequiredService<IRenovarTokenUseCase>();
    Result<LoginResult> resultado = null!;
    var excecao = await Record.ExceptionAsync(async () =>
        resultado = await renovar1.ExecutarAsync(refreshPlanoA, CancellationToken.None));

    Assert.Null(excecao);
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

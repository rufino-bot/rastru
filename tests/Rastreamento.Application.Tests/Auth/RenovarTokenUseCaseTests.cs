using Microsoft.Extensions.Logging;
using Rastreamento.Application.Auth;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Auth;

public class RenovarTokenUseCaseTests
{
  private static readonly FakeTokenHasher Hasher = new();

  private readonly FakeLogger<RenovarTokenUseCase> _logger = new();

  private (RenovarTokenUseCase uc, FakeRefreshTokenRepo repo) Montar(RefreshToken? ativo)
  {
    var repo = new FakeRefreshTokenRepo { Ativo = ativo };
    var emissor = new EmissorDeSessao(repo, Hasher, new FakeAccessTokenGenerator(), FakeJwtOptions.Instance);
    var uc = new RenovarTokenUseCase(repo, Hasher, emissor, _logger);
    return (uc, repo);
  }

  private static RefreshToken TokenAtivo() => new()
  {
    Id = 5,
    UsuarioId = 1,
    TokenHash = Hasher.Hash("plano-antigo"),
    CriadoEm = DateTime.UtcNow.AddMinutes(-1),
    ExpiraEm = DateTime.UtcNow.AddDays(7),
    RevogadoEm = null,
    // Ativo = true e OBRIGATORIO: bool default e false, e RenovarTokenUseCase checa
    // !atual.Usuario.Ativo. Sem isso, todo teste de caminho feliz falha.
    Usuario = new Usuario
    {
      Id = 1,
      NomeUsuario = "admin",
      NomeCompleto = "Administrador do Sistema",
      Ativo = true,
      Perfil = new Perfil { Nome = "Administrador" }
    }
  };

  [Fact]
  public async Task Refresh_valido_rotaciona_e_revoga_o_antigo()
  {
    var (uc, repo) = Montar(TokenAtivo());

    var r = await uc.ExecutarAsync("plano-antigo", default);

    Assert.True(r.Sucesso);
    Assert.NotNull(repo.Ativo!.RevogadoEm);   // antigo revogado
    Assert.Single(repo.Adicionados);          // novo persistido

    // Aponta para o token NOVO — e o hash tem que bater com o do token realmente emitido,
    // nao um re-hash independente (era o defeito do seam antigo).
    Assert.Equal(Hasher.Hash(r.Valor!.RefreshTokenPlano), repo.Ativo.SubstituidoPorTokenHash);
    Assert.Equal(repo.Adicionados[0].TokenHash, repo.Ativo.SubstituidoPorTokenHash);

    // Atomicidade: revogacao + emissao commitam juntas, num unico save.
    Assert.Equal(1, repo.Saves);
  }

  [Fact]
  public async Task Refresh_de_token_revogado_e_reuso_queima_a_familia_do_usuario()
  {
    // Cenario de roubo: o ladrao usou o token A primeiro e recebeu o B. Quando o legitimo
    // reapresenta o A, o refresh vazou — e derrubar so o A deixaria a sessao B do ladrao viva.
    var reapresentado = TokenAtivo();
    reapresentado.RevogadoEm = DateTime.UtcNow.AddMinutes(-5);

    var (uc, repo) = Montar(reapresentado);
    var doLadrao = new RefreshToken
    {
      Id = 6,
      UsuarioId = 1,
      TokenHash = Hasher.Hash("plano-do-ladrao"),
      CriadoEm = DateTime.UtcNow.AddMinutes(-5),
      ExpiraEm = DateTime.UtcNow.AddDays(7),
    };
    repo.Adicionados.Add(doLadrao);

    var r = await uc.ExecutarAsync("plano-antigo", default);

    Assert.False(r.Sucesso);
    Assert.Equal(new[] { 1 }, repo.RevogacoesEmMassa);
    Assert.NotNull(doLadrao.RevogadoEm);   // a sessao do ladrao cai junto
                                           // Nao emite sessao nova: o unico "Adicionados" e o token do ladrao que o teste plantou.
    Assert.Single(repo.Adicionados);
    Assert.Null(r.Valor);
  }

  [Fact]
  public async Task Reuso_loga_warning_sem_expor_o_token()
  {
    var reapresentado = TokenAtivo();
    reapresentado.RevogadoEm = DateTime.UtcNow.AddMinutes(-5);
    var (uc, _) = Montar(reapresentado);

    await uc.ExecutarAsync("plano-antigo", default);

    var entrada = Assert.Single(_logger.Entradas);
    Assert.Equal(LogLevel.Warning, entrada.Nivel);
    Assert.Contains("euso", entrada.Mensagem);  // "Reuso"/"reuso", sem depender da caixa
                                                // Nunca logar segredo: nem o token em texto plano, nem o hash dele.
    Assert.DoesNotContain("plano-antigo", entrada.Mensagem);
    Assert.DoesNotContain(Hasher.Hash("plano-antigo"), entrada.Mensagem);
  }

  [Fact]
  public async Task Refresh_expirado_falha_e_nao_queima_a_familia()
  {
    // Expiracao nao e sinal de roubo: queimar a familia ali transformaria um refresh
    // atrasado numa deslogada geral.
    var expirado = TokenAtivo();
    expirado.ExpiraEm = DateTime.UtcNow.AddMinutes(-1);
    var (uc, repo) = Montar(expirado);

    var r = await uc.ExecutarAsync("plano-antigo", default);

    Assert.False(r.Sucesso);
    Assert.Empty(repo.Adicionados);
    Assert.Null(repo.Ativo!.RevogadoEm);
    Assert.Empty(repo.RevogacoesEmMassa);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Refresh_de_usuario_desativado_falha_e_nao_queima_a_familia()
  {
    // Desativacao tambem nao e sinal de roubo — mesma razao do teste acima — mas
    // `!atual.Usuario.Ativo` tem que ser checado mesmo assim: desativar um usuario tem que
    // expulsa-lo do sistema. Sem esta checagem ele continuaria renovando a sessao ate o
    // refresh expirar sozinho (ate 7 dias).
    var desativado = TokenAtivo();
    desativado.Usuario.Ativo = false;
    var (uc, repo) = Montar(desativado);

    var r = await uc.ExecutarAsync("plano-antigo", default);

    Assert.False(r.Sucesso);
    Assert.Empty(repo.Adicionados);
    Assert.Null(repo.Ativo!.RevogadoEm);   // nao rotaciona
    Assert.Empty(repo.RevogacoesEmMassa);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Refresh_inexistente_falha_e_nao_queima_nada()
  {
    var (uc, repo) = Montar(null);

    var r = await uc.ExecutarAsync("qualquer", default);

    Assert.False(r.Sucesso);
    Assert.Empty(repo.Adicionados);
    Assert.Empty(repo.RevogacoesEmMassa);
    Assert.Equal(0, repo.Saves);
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public async Task Refresh_vazio_falha_sem_consultar_o_repositorio(string plano)
  {
    var (uc, repo) = Montar(TokenAtivo());

    var r = await uc.ExecutarAsync(plano, default);

    Assert.False(r.Sucesso);
    Assert.Empty(repo.Adicionados);
    Assert.Null(repo.Ativo!.RevogadoEm);
  }

  [Fact]
  public async Task Falhas_nao_revelam_qual_condicao_falhou()
  {
    var expirado = TokenAtivo();
    expirado.ExpiraEm = DateTime.UtcNow.AddMinutes(-1);

    var desativado = TokenAtivo();
    desativado.Usuario.Ativo = false;

    var reusado = TokenAtivo();
    reusado.RevogadoEm = DateTime.UtcNow.AddMinutes(-5);

    var falhas = new List<Result<LoginResult>>();
    foreach (var cenario in new RefreshToken?[] { null, expirado, desativado, reusado })
    {
      var (uc, _) = Montar(cenario);
      falhas.Add(await uc.ExecutarAsync("plano-antigo", default));
    }

    // Token vazio tambem nao pode se distinguir dos demais.
    var (ucVazio, _) = Montar(TokenAtivo());
    falhas.Add(await ucVazio.ExecutarAsync("", default));

    Assert.Single(falhas.Select(f => f.Erro).Distinct());
    // O tipo do erro tambem e unico: e ele que decide o status HTTP, entao variar aqui
    // vazaria a condicao que falhou pelo codigo de resposta.
    Assert.Single(falhas.Select(f => f.TipoDoErro).Distinct());
    Assert.All(falhas, f => Assert.Equal(TipoDeErro.NaoAutorizado, f.TipoDoErro));
  }
}

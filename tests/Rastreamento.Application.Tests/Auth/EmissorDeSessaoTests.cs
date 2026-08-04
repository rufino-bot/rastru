using Microsoft.Extensions.Options;
using Rastreamento.Application.Auth;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Auth;

public class EmissorDeSessaoTests
{
  private static readonly FakeTokenHasher Hasher = new();

  private static EmissorDeSessao NovoEmissor(FakeRefreshTokenRepo repo) =>
      new(repo, Hasher, new FakeAccessTokenGenerator(), FakeJwtOptions.Instance);

  private static Usuario Admin() => new()
  {
    Id = 1,
    NomeUsuario = "admin",
    NomeCompleto = "Administrador do Sistema",
    Ativo = true,
    Perfil = new Perfil { Nome = "Administrador" }
  };

  [Fact]
  public async Task Emitir_persiste_apenas_o_hash_e_salva_uma_vez()
  {
    var repo = new FakeRefreshTokenRepo();

    var sessao = await NovoEmissor(repo).EmitirAsync(Admin(), default);

    Assert.False(string.IsNullOrWhiteSpace(sessao.RefreshTokenPlano));

    var persistido = Assert.Single(repo.Adicionados);
    Assert.Equal(1, persistido.UsuarioId);
    // O refresh token so pode ser persistido em forma de hash (nunca em texto plano).
    Assert.Equal(Hasher.Hash(sessao.RefreshTokenPlano), persistido.TokenHash);
    Assert.NotEqual(sessao.RefreshTokenPlano, persistido.TokenHash);

    // Datas sempre em UTC nesta camada.
    Assert.Equal(DateTimeKind.Utc, persistido.CriadoEm.Kind);
    Assert.Equal(DateTimeKind.Utc, persistido.ExpiraEm.Kind);
    Assert.Equal(sessao.RefreshTokenExpiraEm, persistido.ExpiraEm);
    Assert.Null(persistido.RevogadoEm);

    Assert.Equal(1, repo.Saves);
  }

  [Fact]
  public async Task Rotacionar_revoga_o_antigo_apontando_para_o_novo_num_unico_save()
  {
    var repo = new FakeRefreshTokenRepo();
    var atual = new RefreshToken
    {
      Id = 5,
      UsuarioId = 1,
      TokenHash = Hasher.Hash("plano-antigo"),
      CriadoEm = DateTime.UtcNow.AddMinutes(-1),
      ExpiraEm = DateTime.UtcNow.AddDays(7),
      Usuario = Admin()
    };

    var sessao = await NovoEmissor(repo).RotacionarAsync(atual, default);

    var novo = Assert.Single(repo.Adicionados);
    Assert.NotNull(atual.RevogadoEm);
    Assert.Equal(DateTimeKind.Utc, atual.RevogadoEm!.Value.Kind);

    // O ponteiro de auditoria usa o hash do token REALMENTE emitido — nao um re-hash.
    Assert.Equal(novo.TokenHash, atual.SubstituidoPorTokenHash);
    Assert.Equal(Hasher.Hash(sessao.RefreshTokenPlano), atual.SubstituidoPorTokenHash);

    // Atomicidade: revogacao do antigo e emissao do novo commitam juntas.
    Assert.Equal(1, repo.Saves);
  }

  [Theory]
  [InlineData(0, 15)]
  [InlineData(-1, 15)]
  [InlineData(7, 0)]
  [InlineData(7, -1)]
  public void Configuracao_invalida_falha_na_construcao(int refreshTokenDays, int accessTokenMinutes)
  {
    var opcoes = Options.Create(new JwtOptions
    {
      RefreshTokenDays = refreshTokenDays,
      AccessTokenMinutes = accessTokenMinutes
    });

    Assert.Throws<ArgumentOutOfRangeException>(() =>
        new EmissorDeSessao(new FakeRefreshTokenRepo(), Hasher, new FakeAccessTokenGenerator(), opcoes));
  }
}

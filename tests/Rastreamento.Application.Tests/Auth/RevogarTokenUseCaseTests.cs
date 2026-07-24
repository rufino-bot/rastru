using Rastreamento.Application.Auth;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Auth;

public class RevogarTokenUseCaseTests
{
    private static readonly FakeTokenHasher Hasher = new();

    private static RefreshToken TokenAtivo() => new()
    {
        Id = 9,
        UsuarioId = 1,
        TokenHash = Hasher.Hash("plano"),
        CriadoEm = DateTime.UtcNow,
        ExpiraEm = DateTime.UtcNow.AddDays(1),
        RevogadoEm = null
    };

    [Fact]
    public async Task Logout_revoga_token_existente()
    {
        var repo = new FakeRefreshTokenRepo { Ativo = TokenAtivo() };
        var uc = new RevogarTokenUseCase(repo, Hasher);

        var r = await uc.ExecutarAsync("plano", default);

        Assert.True(r.Sucesso);
        Assert.NotNull(repo.Ativo!.RevogadoEm);
        Assert.Equal(DateTimeKind.Utc, repo.Ativo.RevogadoEm!.Value.Kind);
        Assert.Equal(1, repo.Saves);
    }

    [Fact]
    public async Task Logout_token_inexistente_e_sucesso_sem_salvar()
    {
        // "Nao havia nada para revogar" e sucesso: sinalizar falha aqui vazaria a existencia
        // do token e quebraria a idempotencia que o endpoint de logout promete.
        var repo = new FakeRefreshTokenRepo { Ativo = null };
        var uc = new RevogarTokenUseCase(repo, Hasher);

        var r = await uc.ExecutarAsync("nada", default);

        Assert.True(r.Sucesso);
        Assert.Null(r.Erro);
        Assert.Equal(0, repo.Saves);
    }

    [Fact]
    public async Task Logout_e_idempotente_para_token_ja_revogado()
    {
        // ObterAtivoPorHashAsync so devolve token nao revogado, entao o segundo logout
        // nao encontra nada — nao lanca e nao regrava RevogadoEm.
        var repo = new FakeRefreshTokenRepo { Ativo = TokenAtivo() };
        var uc = new RevogarTokenUseCase(repo, Hasher);

        await uc.ExecutarAsync("plano", default);
        var revogadoEm = repo.Ativo!.RevogadoEm;

        var ex = await Record.ExceptionAsync(() => uc.ExecutarAsync("plano", default));

        Assert.Null(ex);
        Assert.Equal(revogadoEm, repo.Ativo.RevogadoEm);
        Assert.Equal(1, repo.Saves);   // o segundo logout nao commita nada
    }

    [Fact]
    public async Task Logout_de_token_expirado_revoga_mesmo_assim()
    {
        // ObterAtivoPorHashAsync nao filtra por expiracao; revogar um token ja expirado
        // e inofensivo e mantem o logout idempotente do ponto de vista do cliente.
        var expirado = TokenAtivo();
        expirado.ExpiraEm = DateTime.UtcNow.AddMinutes(-1);
        var repo = new FakeRefreshTokenRepo { Ativo = expirado };
        var uc = new RevogarTokenUseCase(repo, Hasher);

        var ex = await Record.ExceptionAsync(() => uc.ExecutarAsync("plano", default));

        Assert.Null(ex);
        Assert.NotNull(repo.Ativo!.RevogadoEm);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Logout_com_token_vazio_nao_lanca_e_nao_salva(string plano)
    {
        var repo = new FakeRefreshTokenRepo { Ativo = TokenAtivo() };
        var uc = new RevogarTokenUseCase(repo, Hasher);

        var ex = await Record.ExceptionAsync(() => uc.ExecutarAsync(plano, default));

        Assert.Null(ex);
        Assert.Null(repo.Ativo!.RevogadoEm);
        Assert.Equal(0, repo.Saves);
    }
}

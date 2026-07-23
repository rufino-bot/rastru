using Rastreamento.Application.Auth;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Auth;

public class AutenticarUsuarioUseCaseTests
{
    private const string SenhaCorreta = "Admin@123";

    // Mensagem generica: falha de login nao pode revelar QUAL condicao falhou.
    private const string ErroGenerico = "Usuário ou senha inválidos.";

    private static readonly FakePasswordHasher Hasher = new();

    private static Usuario AdminAtivo() => new()
    {
        Id = 1,
        NomeUsuario = "admin",
        NomeCompleto = "Administrador do Sistema",
        Ativo = true,
        SenhaHash = Hasher.Hash(SenhaCorreta),
        Perfil = new Perfil { Nome = "Administrador" }
    };

    private static AutenticarUsuarioUseCase NovoUseCase(Usuario? usuario, out FakeRefreshTokenRepo refreshRepo)
    {
        refreshRepo = new FakeRefreshTokenRepo();
        var emissor = new EmissorDeSessao(
            refreshRepo,
            new FakeTokenHasher(),
            new FakeAccessTokenGenerator(),
            FakeJwtOptions.Instance);
        return new AutenticarUsuarioUseCase(new FakeUsuarioRepo(usuario), Hasher, emissor);
    }

    [Fact]
    public async Task Login_valido_retorna_tokens_e_persiste_refresh()
    {
        var uc = NovoUseCase(AdminAtivo(), out var refreshRepo);

        var r = await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

        Assert.True(r.Sucesso);
        Assert.Null(r.Erro);
        Assert.Equal("access-admin", r.Valor!.AccessToken);
        Assert.Equal(1, r.Valor.Usuario.Id);
        Assert.Equal("admin", r.Valor.Usuario.NomeUsuario);
        Assert.Equal("Administrador do Sistema", r.Valor.Usuario.NomeCompleto);
        Assert.Equal("Administrador", r.Valor.Usuario.Perfil);
        Assert.False(string.IsNullOrWhiteSpace(r.Valor.RefreshTokenPlano));

        var persistido = Assert.Single(refreshRepo.Adicionados);
        Assert.Equal(1, persistido.UsuarioId);
        // O refresh token so pode ser persistido em forma de hash (nunca em texto plano).
        Assert.Equal("sha:" + r.Valor.RefreshTokenPlano, persistido.TokenHash);
        Assert.NotEqual(r.Valor.RefreshTokenPlano, persistido.TokenHash);
        Assert.Equal(r.Valor.RefreshTokenExpiraEm, persistido.ExpiraEm);
        Assert.Null(persistido.RevogadoEm);

        // Datas sempre em UTC nesta camada.
        Assert.Equal(DateTimeKind.Utc, persistido.CriadoEm.Kind);
        Assert.Equal(DateTimeKind.Utc, persistido.ExpiraEm.Kind);
        Assert.True(
            Math.Abs((persistido.ExpiraEm - DateTime.UtcNow.AddDays(7)).TotalMinutes) < 1,
            $"ExpiraEm ({persistido.ExpiraEm:o}) deveria estar a ~7 dias de UtcNow ({DateTime.UtcNow:o}).");
    }

    [Fact]
    public async Task Login_senha_errada_falha_sem_persistir()
    {
        var uc = NovoUseCase(AdminAtivo(), out var refreshRepo);

        var r = await uc.ExecutarAsync(new LoginRequest("admin", "errada"), default);

        Assert.False(r.Sucesso);
        Assert.Null(r.Valor);
        Assert.Equal(ErroGenerico, r.Erro);
        Assert.Empty(refreshRepo.Adicionados);
    }

    [Fact]
    public async Task Login_usuario_inexistente_falha()
    {
        var uc = NovoUseCase(null, out var refreshRepo);

        var r = await uc.ExecutarAsync(new LoginRequest("ninguem", "x"), default);

        Assert.False(r.Sucesso);
        Assert.Equal(ErroGenerico, r.Erro);
        Assert.Empty(refreshRepo.Adicionados);
    }

    [Fact]
    public async Task Login_usuario_inativo_falha()
    {
        var inativo = AdminAtivo();
        inativo.Ativo = false;
        var uc = NovoUseCase(inativo, out var refreshRepo);

        var r = await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

        Assert.False(r.Sucesso);
        Assert.Equal(ErroGenerico, r.Erro);
        Assert.Empty(refreshRepo.Adicionados);
    }
}

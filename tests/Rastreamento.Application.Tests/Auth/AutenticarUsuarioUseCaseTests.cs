using Rastreamento.Application.Auth;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Auth;

public class AutenticarUsuarioUseCaseTests
{
    private const string SenhaCorreta = "Admin@123";

    // Mensagem generica: falha de login nao pode revelar QUAL condicao falhou.
    private const string ErroGenerico = "Usuário ou senha inválidos.";

    // Instancia por teste (o xUnit cria uma instancia da classe por [Fact]): os contadores do
    // fake nao vazam de um teste para o outro.
    private readonly FakePasswordHasher _hasher = new();

    private Usuario AdminAtivo() => new()
    {
        Id = 1,
        NomeUsuario = "admin",
        NomeCompleto = "Administrador do Sistema",
        Ativo = true,
        SenhaHash = _hasher.Hash(SenhaCorreta),
        Perfil = new Perfil { Nome = "Administrador" }
    };

    private AutenticarUsuarioUseCase NovoUseCase(Usuario? usuario, out FakeRefreshTokenRepo refreshRepo)
    {
        refreshRepo = new FakeRefreshTokenRepo();
        var emissor = new EmissorDeSessao(
            refreshRepo,
            new FakeTokenHasher(),
            new FakeAccessTokenGenerator(),
            FakeJwtOptions.Instance);
        return new AutenticarUsuarioUseCase(new FakeUsuarioRepo(usuario), _hasher, emissor);
    }

    [Fact]
    public async Task Login_valido_retorna_tokens_e_persiste_refresh()
    {
        var uc = NovoUseCase(AdminAtivo(), out var refreshRepo);

        var r = await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

        Assert.True(r.Sucesso);
        Assert.Null(r.Erro);
        Assert.Null(r.TipoDoErro);
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
        Assert.Equal(TipoDeErro.NaoAutorizado, r.TipoDoErro);
        Assert.Empty(refreshRepo.Adicionados);
    }

    [Fact]
    public async Task Login_usuario_inexistente_falha()
    {
        var uc = NovoUseCase(null, out var refreshRepo);

        var r = await uc.ExecutarAsync(new LoginRequest("ninguem", "x"), default);

        Assert.False(r.Sucesso);
        Assert.Equal(ErroGenerico, r.Erro);
        Assert.Equal(TipoDeErro.NaoAutorizado, r.TipoDoErro);
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
        Assert.Equal(TipoDeErro.NaoAutorizado, r.TipoDoErro);
        Assert.Empty(refreshRepo.Adicionados);
    }

    // ----- Trabalho constante (sem oraculo de timing) --------------------------------------
    //
    // Comparar os corpos das respostas nao prova nada sobre timing: os corpos ja eram identicos
    // enquanto o caminho de miss retornava sem passar pelo BCrypt. O que da para checar de forma
    // deterministica e que a verificacao de senha acontece nos tres caminhos, contra um hash de
    // mesmo custo — e e isso que estes testes fazem.

    [Fact]
    public async Task Login_com_usuario_inexistente_ainda_verifica_a_senha()
    {
        var uc = NovoUseCase(null, out _);

        await uc.ExecutarAsync(new LoginRequest("ninguem", "x"), default);

        Assert.Equal(1, _hasher.Verificacoes);
        Assert.Equal(_hasher.HashFicticio, _hasher.UltimoHashVerificado);
    }

    [Fact]
    public async Task Login_com_usuario_inativo_ainda_verifica_a_senha()
    {
        var inativo = AdminAtivo();
        inativo.Ativo = false;
        var uc = NovoUseCase(inativo, out _);

        await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

        Assert.Equal(1, _hasher.Verificacoes);
        Assert.Equal(_hasher.HashFicticio, _hasher.UltimoHashVerificado);
    }

    [Fact]
    public async Task Login_valido_verifica_a_senha_uma_unica_vez_contra_o_hash_do_usuario()
    {
        var admin = AdminAtivo();
        var uc = NovoUseCase(admin, out _);

        await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

        // Mesmo numero de verificacoes dos caminhos de falha: um BCrypt, nem mais nem menos.
        Assert.Equal(1, _hasher.Verificacoes);
        Assert.Equal(admin.SenhaHash, _hasher.UltimoHashVerificado);
    }
}

using Rastreamento.Application.Auth;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Auth;

public class RenovarTokenUseCaseTests
{
    private static readonly FakeTokenHasher Hasher = new();

    private static (RenovarTokenUseCase uc, FakeRefreshTokenRepo repo) Montar(RefreshToken? ativo)
    {
        var repo = new FakeRefreshTokenRepo { Ativo = ativo };
        var emissor = new EmissorDeSessao(repo, Hasher, new FakeAccessTokenGenerator(), FakeJwtOptions.Instance);
        var uc = new RenovarTokenUseCase(repo, Hasher, emissor);
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
    public async Task Refresh_revogado_falha_e_nao_emite()
    {
        var revogado = TokenAtivo();
        revogado.RevogadoEm = DateTime.UtcNow.AddMinutes(-5);
        var (uc, repo) = Montar(revogado);

        var r = await uc.ExecutarAsync("plano-antigo", default);

        Assert.False(r.Sucesso);
        Assert.Empty(repo.Adicionados);
        Assert.Equal(0, repo.Saves);
    }

    [Fact]
    public async Task Refresh_de_usuario_desativado_falha_e_nao_emite()
    {
        // Desativar um usuario tem que expulsa-lo do sistema. Sem esta checagem ele
        // continuaria renovando a sessao ate o refresh expirar sozinho (ate 7 dias).
        var token = TokenAtivo();
        token.Usuario.Ativo = false;
        var (uc, repo) = Montar(token);

        var r = await uc.ExecutarAsync("plano-antigo", default);

        Assert.False(r.Sucesso);
        Assert.Empty(repo.Adicionados);
        Assert.Null(repo.Ativo!.RevogadoEm);   // nao rotaciona
        Assert.Equal(0, repo.Saves);
    }

    [Fact]
    public async Task Refresh_expirado_falha()
    {
        var expirado = TokenAtivo();
        expirado.ExpiraEm = DateTime.UtcNow.AddMinutes(-1);
        var (uc, repo) = Montar(expirado);

        var r = await uc.ExecutarAsync("plano-antigo", default);

        Assert.False(r.Sucesso);
        Assert.Empty(repo.Adicionados);
        Assert.Null(repo.Ativo!.RevogadoEm);
    }

    [Fact]
    public async Task Refresh_inexistente_falha()
    {
        var (uc, repo) = Montar(null);

        var r = await uc.ExecutarAsync("qualquer", default);

        Assert.False(r.Sucesso);
        Assert.Empty(repo.Adicionados);
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

        var erros = new List<string?>();
        foreach (var cenario in new RefreshToken?[] { null, expirado, desativado })
        {
            var (uc, _) = Montar(cenario);
            erros.Add((await uc.ExecutarAsync("plano-antigo", default)).Erro);
        }

        // Token vazio tambem nao pode se distinguir dos demais.
        var (ucVazio, _) = Montar(TokenAtivo());
        erros.Add((await ucVazio.ExecutarAsync("", default)).Erro);

        Assert.Single(erros.Distinct());
    }
}

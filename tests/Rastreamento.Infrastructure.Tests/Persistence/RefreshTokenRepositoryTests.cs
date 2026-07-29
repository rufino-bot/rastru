using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Requer o SQL Server no ar (docker compose up -d) com schema e seed aplicados.
/// Usa dois usuarios proprios, criados e removidos pelo teste: `RevogarTodosAtivosDoUsuarioAsync`
/// queima TODOS os tokens ativos de um usuario, entao roda-lo contra o `admin` do seed derrubaria
/// os tokens de outros testes rodando em paralelo (AuthEndpointsTests tambem autentica como
/// `admin`, inclusive em outro processo quando `dotnet test` roda os projetos concorrentemente) —
/// e a contagem devolvida ficaria nao-deterministica.
/// </summary>
public class RefreshTokenRepositoryTests : TesteComBanco, IAsyncLifetime
{
    private int _usuarioId;
    private int _usuarioVizinhoId;

    public async Task InitializeAsync()
    {
        await using var db = NovoContexto();
        var perfil = await db.Perfis.SingleAsync(p => p.Nome == "Administrador");

        // Nome unico por execucao: UQ_Usuario_NomeUsuario nao perdoa sobra de uma execucao
        // anterior que tenha morrido antes da limpeza.
        var usuario = new Usuario
        {
            NomeUsuario = $"repo-{Guid.NewGuid():N}",
            SenhaHash = "nao-usado-neste-teste",
            NomeCompleto = "Usuario de Teste do Repositorio",
            PerfilId = perfil.Id,
            Ativo = true,
        };
        var vizinho = new Usuario
        {
            NomeUsuario = $"repo-viz-{Guid.NewGuid():N}",
            SenhaHash = "nao-usado-neste-teste",
            NomeCompleto = "Vizinho de Teste do Repositorio",
            PerfilId = perfil.Id,
            Ativo = true,
        };
        db.Usuarios.AddRange(usuario, vizinho);
        await db.SaveChangesAsync();
        _usuarioId = usuario.Id;
        _usuarioVizinhoId = vizinho.Id;
    }

    public async Task DisposeAsync()
    {
        await using var db = NovoContexto();
        // RefreshToken tem FK para Usuario: as linhas filhas saem primeiro.
        db.RefreshTokens.RemoveRange(await db.RefreshTokens
            .Where(t => t.UsuarioId == _usuarioId || t.UsuarioId == _usuarioVizinhoId).ToListAsync());
        db.Usuarios.RemoveRange(await db.Usuarios
            .Where(u => u.Id == _usuarioId || u.Id == _usuarioVizinhoId).ToListAsync());
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ObterPorHashAsync_enxerga_token_revogado_que_ObterAtivoPorHashAsync_nao_ve()
    {
        var hash = await CriarTokenAsync(revogadoEm: DateTime.UtcNow.AddMinutes(-5));

        await using var db = NovoContexto();
        var repo = new RefreshTokenRepository(db);

        // A diferenca entre os dois metodos E o ponto: sem ObterPorHashAsync nao ha como
        // perceber que um token ja rotacionado foi reapresentado.
        Assert.Null(await repo.ObterAtivoPorHashAsync(hash, default));

        var encontrado = await repo.ObterPorHashAsync(hash, default);
        Assert.NotNull(encontrado);
        Assert.NotNull(encontrado!.RevogadoEm);
        Assert.Equal(_usuarioId, encontrado.UsuarioId);
        // Usuario + Perfil vem junto: o caso de uso precisa de Usuario.Ativo e o caminho feliz
        // da rotacao precisa do nome do Perfil para a claim `role`.
        Assert.NotNull(encontrado.Usuario);
        Assert.Equal("Administrador", encontrado.Usuario.Perfil.Nome);
    }

    [Fact]
    public async Task RevogarTodosAtivosDoUsuarioAsync_revoga_so_os_ativos_e_devolve_a_contagem()
    {
        var ativoA = await CriarTokenAsync(revogadoEm: null);
        var ativoB = await CriarTokenAsync(revogadoEm: null);
        var revogadoAntes = DateTime.UtcNow.AddHours(-1);
        var jaRevogado = await CriarTokenAsync(revogadoEm: revogadoAntes);

        var agora = DateTime.UtcNow;
        int revogados;
        await using (var db = NovoContexto())
            revogados = await new RefreshTokenRepository(db)
                .RevogarTodosAtivosDoUsuarioAsync(_usuarioId, agora, default);

        Assert.Equal(2, revogados);

        await using var leitura = NovoContexto();
        foreach (var hash in new[] { ativoA, ativoB })
        {
            var linha = await leitura.RefreshTokens.AsNoTracking().SingleAsync(t => t.TokenHash == hash);
            Assert.Equal(agora, linha.RevogadoEm!.Value, TimeSpan.FromSeconds(1));
        }

        // O que ja estava revogado mantem a data original: a queima nao reescreve historico.
        var antigo = await leitura.RefreshTokens.AsNoTracking().SingleAsync(t => t.TokenHash == jaRevogado);
        Assert.Equal(revogadoAntes, antigo.RevogadoEm!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RevogarTodosAtivosDoUsuarioAsync_nao_toca_em_token_de_outro_usuario()
    {
        var meu = await CriarTokenAsync(revogadoEm: null);

        // O segundo usuario da classe serve de vizinho: o filtro por UsuarioId tem que isolar a
        // queima. DisposeAsync remove os tokens de ambos os usuarios, entao nao precisa de
        // try/finally aqui.
        var hashDoVizinho = $"vizinho-{Guid.NewGuid():N}";
        await using (var db = NovoContexto())
        {
            db.RefreshTokens.Add(new RefreshToken
            {
                UsuarioId = _usuarioVizinhoId,
                TokenHash = hashDoVizinho,
                CriadoEm = DateTime.UtcNow,
                ExpiraEm = DateTime.UtcNow.AddDays(7),
            });
            await db.SaveChangesAsync();
        }

        await using (var db = NovoContexto())
            await new RefreshTokenRepository(db)
                .RevogarTodosAtivosDoUsuarioAsync(_usuarioId, DateTime.UtcNow, default);

        await using var leitura = NovoContexto();
        Assert.NotNull((await leitura.RefreshTokens.AsNoTracking()
            .SingleAsync(t => t.TokenHash == meu)).RevogadoEm);
        Assert.Null((await leitura.RefreshTokens.AsNoTracking()
            .SingleAsync(t => t.TokenHash == hashDoVizinho)).RevogadoEm);
    }

    /// <summary>Insere um refresh token do usuario do teste e devolve o hash usado.</summary>
    private async Task<string> CriarTokenAsync(DateTime? revogadoEm)
    {
        await using var db = NovoContexto();
        var hash = $"teste-{Guid.NewGuid():N}";
        db.RefreshTokens.Add(new RefreshToken
        {
            UsuarioId = _usuarioId,
            TokenHash = hash,
            // CriadoEm antes de ExpiraEm: CK_RefreshToken_ExpiraAposCriado exige.
            CriadoEm = DateTime.UtcNow.AddMinutes(-10),
            ExpiraEm = DateTime.UtcNow.AddDays(7),
            RevogadoEm = revogadoEm,
        });
        await db.SaveChangesAsync();
        return hash;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Rastreamento.Infrastructure.Security;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Usuario exclusivo de um teste, criado e removido por ele. Os testes de reuso de refresh token e
/// de lockout tem efeito destrutivo sobre a conta (queimar todas as sessoes; trancar por 15 min):
/// rodar contra o `admin` do seed faria um teste derrubar os outros — inclusive de outra classe,
/// que o xUnit roda em paralelo por padrao.
/// </summary>
public sealed class UsuarioDeTeste : IAsyncDisposable
{
    public const string Senha = "Senha@123";

    private readonly IServiceProvider _servicos;

    public string NomeUsuario { get; }

    public int Id { get; private set; }

    private UsuarioDeTeste(IServiceProvider servicos, string nomeUsuario)
    {
        _servicos = servicos;
        NomeUsuario = nomeUsuario;
    }

    /// <param name="prefixo">Rotulo curto (ate 17 caracteres) para identificar o teste dono.</param>
    public static async Task<UsuarioDeTeste> CriarAsync(IServiceProvider servicos, string prefixo)
    {
        // Nome unico por execucao: UQ_Usuario_NomeUsuario nao perdoa sobra de uma execucao anterior
        // que tenha morrido antes da limpeza. prefixo(<=17) + '-' + 32 hex cabe no NVARCHAR(50).
        var usuario = new UsuarioDeTeste(servicos, $"{prefixo}-{Guid.NewGuid():N}");

        using var escopo = servicos.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
        var perfil = await db.Perfis.SingleAsync(p => p.Nome == "Administrador");

        var linha = new Usuario
        {
            NomeUsuario = usuario.NomeUsuario,
            // BCrypt real: o login em producao roda o hasher de verdade, entao o hash tem que ser.
            SenhaHash = new BCryptPasswordHasher().Hash(Senha),
            NomeCompleto = "Usuario de Teste",
            PerfilId = perfil.Id,
            Ativo = true,
        };

        db.Usuarios.Add(linha);
        await db.SaveChangesAsync();
        usuario.Id = linha.Id;
        return usuario;
    }

    public async ValueTask DisposeAsync()
    {
        using var escopo = _servicos.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();

        // RefreshToken tem FK para Usuario: as linhas filhas saem primeiro.
        db.RefreshTokens.RemoveRange(await db.RefreshTokens.Where(t => t.UsuarioId == Id).ToListAsync());
        db.Usuarios.RemoveRange(await db.Usuarios.Where(u => u.Id == Id).ToListAsync());
        await db.SaveChangesAsync();
    }
}

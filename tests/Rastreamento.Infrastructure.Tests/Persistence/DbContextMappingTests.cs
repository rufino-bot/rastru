using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

public class DbContextMappingTests
{
    // Requer o SQL Server da Task 2 no ar (docker compose up -d) com seed aplicado.
    private const string Conn =
        "Server=localhost,1433;Database=Rastreamento;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True";

    private static RastreamentoDbContext NovoContexto()
    {
        var options = new DbContextOptionsBuilder<RastreamentoDbContext>()
            .UseSqlServer(Conn).Options;
        return new RastreamentoDbContext(options);
    }

    [Fact]
    public async Task Mapeia_seis_perfis_seedados()
    {
        await using var db = NovoContexto();
        var total = await db.Perfis.CountAsync();
        Assert.Equal(6, total);
    }

    [Fact]
    public async Task Carrega_admin_com_perfil_navegacao()
    {
        await using var db = NovoContexto();
        var admin = await db.Usuarios.Include(u => u.Perfil)
            .SingleAsync(u => u.NomeUsuario == "admin");
        Assert.Equal("Administrador", admin.Perfil.Nome);
    }

    [Fact]
    public async Task Mapeia_refresh_token_com_round_trip_e_navegacao_usuario()
    {
        await using var db = NovoContexto();
        var admin = await db.Usuarios.SingleAsync(u => u.NomeUsuario == "admin");

        var tokenHash = $"teste-{Guid.NewGuid():N}";
        var expiraEm = DateTime.UtcNow.AddDays(7);
        var criadoEm = DateTime.UtcNow;

        var refreshToken = new RefreshToken
        {
            UsuarioId = admin.Id,
            TokenHash = tokenHash,
            ExpiraEm = expiraEm,
            CriadoEm = criadoEm,
            RevogadoEm = null,
            SubstituidoPorTokenHash = null,
        };

        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync();
        var idInserido = refreshToken.Id;

        try
        {
            await using var dbLeitura = NovoContexto();
            var carregado = await dbLeitura.RefreshTokens.Include(t => t.Usuario)
                .SingleAsync(t => t.Id == idInserido);

            Assert.Equal(admin.Id, carregado.UsuarioId);
            Assert.Equal(tokenHash, carregado.TokenHash);
            Assert.Equal(expiraEm, carregado.ExpiraEm, TimeSpan.FromSeconds(1));
            Assert.Equal(criadoEm, carregado.CriadoEm, TimeSpan.FromSeconds(1));
            Assert.Null(carregado.RevogadoEm);
            Assert.Null(carregado.SubstituidoPorTokenHash);
            Assert.Equal("admin", carregado.Usuario.NomeUsuario);
        }
        finally
        {
            await using var dbLimpeza = NovoContexto();
            var paraRemover = await dbLimpeza.RefreshTokens.SingleAsync(t => t.Id == idInserido);
            dbLimpeza.RefreshTokens.Remove(paraRemover);
            await dbLimpeza.SaveChangesAsync();
        }
    }
}

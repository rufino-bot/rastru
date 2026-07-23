using Microsoft.EntityFrameworkCore;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Application.Tests.Persistence;

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
}

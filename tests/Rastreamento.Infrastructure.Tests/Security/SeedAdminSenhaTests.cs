using Microsoft.EntityFrameworkCore;
using Rastreamento.Infrastructure.Persistence;
using Rastreamento.Infrastructure.Security;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Security;

public class SeedAdminSenhaTests
{
    private const string Conn =
        "Server=localhost,1433;Database=Rastreamento;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True";

    [Fact]
    public async Task Senha_do_admin_seedado_valida_contra_Admin123()
    {
        var options = new DbContextOptionsBuilder<RastreamentoDbContext>().UseSqlServer(Conn).Options;
        await using var db = new RastreamentoDbContext(options);
        var admin = await db.Usuarios.SingleAsync(u => u.NomeUsuario == "admin");
        Assert.True(new BCryptPasswordHasher().Verificar("Admin@123", admin.SenhaHash));
    }
}

using Microsoft.EntityFrameworkCore;
using Rastreamento.Infrastructure.Security;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Security;

/// <summary>Requer o SQL Server no ar (docker compose up -d) com schema e seed aplicados.</summary>
public class SeedAdminSenhaTests : TesteComBanco
{
  [Fact]
  public async Task Senha_do_admin_seedado_valida_contra_Admin123()
  {
    await using var db = NovoContexto();
    var admin = await db.Usuarios.SingleAsync(u => u.NomeUsuario == "admin");
    Assert.True(new BCryptPasswordHasher().Verificar("Admin@123", admin.SenhaHash));
  }
}

using Microsoft.EntityFrameworkCore;
using Rastreamento.Infrastructure.Security;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Security;

/// <summary>
/// Os usuarios de perfil do seed entram com o hash certo e o perfil certo, e o hash de cada um e proprio
/// (nao reusa o do admin). Requer o SQL Server no ar (docker compose up -d) com schema e seed aplicados.
/// </summary>
public class SeedUsuariosDePerfilSenhaTests : TesteComBanco
{
  [Theory]
  [InlineData("pcp", "Pcp@123", "PCP")]
  [InlineData("operador", "Operador@123", "Operador")]
  [InlineData("movimentador", "Movimentador@123", "Movimentador")]
  public async Task Usuario_seedado_valida_a_propria_senha_no_proprio_perfil(string nomeUsuario, string senha, string perfil)
  {
    await using var db = NovoContexto();
    var usuario = await db.Usuarios.Include(u => u.Perfil).SingleAsync(u => u.NomeUsuario == nomeUsuario);
    var admin = await db.Usuarios.SingleAsync(u => u.NomeUsuario == "admin");

    Assert.True(new BCryptPasswordHasher().Verificar(senha, usuario.SenhaHash));
    Assert.Equal(perfil, usuario.Perfil.Nome);
    Assert.NotEqual(admin.SenhaHash, usuario.SenhaHash);
  }
}

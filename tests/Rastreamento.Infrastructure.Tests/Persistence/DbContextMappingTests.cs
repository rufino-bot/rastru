using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>Requer o SQL Server no ar (docker compose up -d) com schema e seed aplicados.</summary>
public class DbContextMappingTests : TesteComBanco
{
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
  public async Task Mapeia_as_colunas_de_lockout_do_usuario_com_round_trip()
  {
    await using var db = NovoContexto();
    var perfil = await db.Perfis.SingleAsync(p => p.Nome == "Administrador");

    var bloqueadoAte = DateTime.UtcNow.AddMinutes(15);
    var usuario = new Usuario
    {
      NomeUsuario = $"lockout-{Guid.NewGuid():N}",
      SenhaHash = "nao-usado-neste-teste",
      NomeCompleto = "Usuario de Teste de Lockout",
      PerfilId = perfil.Id,
      Ativo = true,
      FalhasConsecutivas = 3,
      BloqueadoAte = bloqueadoAte,
    };

    db.Usuarios.Add(usuario);
    await db.SaveChangesAsync();
    var id = usuario.Id;

    try
    {
      await using var dbLeitura = NovoContexto();
      var carregado = await dbLeitura.Usuarios.SingleAsync(u => u.Id == id);

      Assert.Equal(3, carregado.FalhasConsecutivas);
      Assert.Equal(bloqueadoAte, carregado.BloqueadoAte!.Value, TimeSpan.FromSeconds(1));
    }
    finally
    {
      await using var dbLimpeza = NovoContexto();
      dbLimpeza.Usuarios.RemoveRange(await dbLimpeza.Usuarios.Where(u => u.Id == id).ToListAsync());
      await dbLimpeza.SaveChangesAsync();
    }
  }

  [Fact]
  public async Task Usuario_novo_nasce_destrancado()
  {
    // INSERT em SQL cru, omitindo as duas colunas de lockout de proposito: e o unico jeito de
    // provar o DEFAULT da coluna (DF_Usuario_FalhasConsecutivas / BloqueadoAte NULL por
    // ausencia de default proprio). UsuarioConfiguration nao declara HasDefaultValue nem
    // ValueGeneratedOnAdd para essas colunas (Database First: o default vive so no .sql, nunca
    // duplicado no mapeamento EF) — e por isso mesmo um INSERT feito pelo EF sempre manda as
    // duas colunas explicitamente e nunca exercitaria o DEFAULT do banco. So o SQL cru prova
    // que o seed e qualquer INSERT que nao mencione essas colunas nasce destrancado.
    await using var db = NovoContexto();
    var perfil = await db.Perfis.SingleAsync(p => p.Nome == "Administrador");
    var nomeUsuario = $"novo-{Guid.NewGuid():N}";

    await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO dbo.Usuario (NomeUsuario, SenhaHash, NomeCompleto, PerfilId, Ativo)
            VALUES ({nomeUsuario}, 'nao-usado-neste-teste', 'Usuario Recem-Criado', {perfil.Id}, 1)");

    var id = await db.Database
        .SqlQuery<int>($"SELECT Id AS [Value] FROM dbo.Usuario WHERE NomeUsuario = {nomeUsuario}")
        .SingleAsync();

    try
    {
      await using var dbLeitura = NovoContexto();
      var carregado = await dbLeitura.Usuarios.AsNoTracking().SingleAsync(u => u.Id == id);

      Assert.Equal(0, carregado.FalhasConsecutivas);
      Assert.Null(carregado.BloqueadoAte);
    }
    finally
    {
      await using var dbLimpeza = NovoContexto();
      dbLimpeza.Usuarios.RemoveRange(await dbLimpeza.Usuarios.Where(u => u.Id == id).ToListAsync());
      await dbLimpeza.SaveChangesAsync();
    }
  }

  [Fact]
  public async Task Mapeia_refresh_token_com_round_trip_e_navegacao_usuario()
  {
    await using var db = NovoContexto();
    var perfil = await db.Perfis.SingleAsync(p => p.Nome == "Administrador");

    // Usuario proprio, e nao o `admin` do seed: a limpeza do AuthEndpointsTests e escopada
    // as linhas do admin, entao um RefreshToken do admin criado aqui ficaria no raio dela
    // quando este projeto roda em paralelo com o Api.Tests.
    var usuario = new Usuario
    {
      NomeUsuario = $"refresh-{Guid.NewGuid():N}",
      SenhaHash = "nao-usado-neste-teste",
      NomeCompleto = "Usuario de Teste de Refresh Token",
      PerfilId = perfil.Id,
      Ativo = true,
    };

    db.Usuarios.Add(usuario);
    await db.SaveChangesAsync();
    var usuarioId = usuario.Id;

    var tokenHash = $"teste-{Guid.NewGuid():N}";
    var criadoEm = DateTime.UtcNow;
    var expiraEm = criadoEm.AddDays(7);

    var refreshToken = new RefreshToken
    {
      UsuarioId = usuarioId,
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

      Assert.Equal(usuarioId, carregado.UsuarioId);
      Assert.Equal(tokenHash, carregado.TokenHash);
      Assert.Equal(expiraEm, carregado.ExpiraEm, TimeSpan.FromSeconds(1));
      Assert.Equal(criadoEm, carregado.CriadoEm, TimeSpan.FromSeconds(1));
      Assert.Null(carregado.RevogadoEm);
      Assert.Null(carregado.SubstituidoPorTokenHash);
      Assert.Equal(usuario.NomeUsuario, carregado.Usuario.NomeUsuario);
    }
    finally
    {
      await using var dbLimpeza = NovoContexto();
      var paraRemover = await dbLimpeza.RefreshTokens.SingleAsync(t => t.Id == idInserido);
      dbLimpeza.RefreshTokens.Remove(paraRemover);
      await dbLimpeza.SaveChangesAsync();

      dbLimpeza.Usuarios.RemoveRange(await dbLimpeza.Usuarios.Where(u => u.Id == usuarioId).ToListAsync());
      await dbLimpeza.SaveChangesAsync();
    }
  }
}

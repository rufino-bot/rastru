using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>Requer o SQL Server no ar (docker compose up -d) com o schema aplicado.</summary>
public class SetorMappingTests : TesteComBanco
{
  [Fact]
  public async Task Mapeia_setor_com_round_trip()
  {
    await using var db = NovoContexto();
    var setor = new Setor { Nome = $"setor-{Guid.NewGuid():N}", Ativo = true };

    db.Setores.Add(setor);
    await db.SaveChangesAsync();
    var id = setor.Id;

    try
    {
      await using var dbLeitura = NovoContexto();
      var carregado = await dbLeitura.Setores.AsNoTracking().SingleAsync(s => s.Id == id);

      Assert.Equal(setor.Nome, carregado.Nome);
      Assert.True(carregado.Ativo);
    }
    finally
    {
      await using var dbLimpeza = NovoContexto();
      dbLimpeza.Setores.RemoveRange(await dbLimpeza.Setores.Where(s => s.Id == id).ToListAsync());
      await dbLimpeza.SaveChangesAsync();
    }
  }

  [Fact]
  public async Task Setor_nasce_ativo_pelo_default_do_banco()
  {
    // INSERT cru omitindo `Ativo` de proposito: e o unico jeito de provar DF_Setor_Ativo.
    // SetorConfiguration nao declara HasDefaultValue (Database First: o default vive so no
    // .sql), entao um INSERT feito pelo EF sempre manda a coluna e nunca exercitaria o DEFAULT.
    await using var db = NovoContexto();
    var nome = $"default-{Guid.NewGuid():N}";

    await db.Database.ExecuteSqlInterpolatedAsync(
        $"INSERT INTO dbo.Setor (Nome) VALUES ({nome})");

    var id = await db.Database
        .SqlQuery<int>($"SELECT Id AS [Value] FROM dbo.Setor WHERE Nome = {nome}").SingleAsync();

    try
    {
      await using var dbLeitura = NovoContexto();
      var carregado = await dbLeitura.Setores.AsNoTracking().SingleAsync(s => s.Id == id);
      Assert.True(carregado.Ativo);
    }
    finally
    {
      await using var dbLimpeza = NovoContexto();
      dbLimpeza.Setores.RemoveRange(await dbLimpeza.Setores.Where(s => s.Id == id).ToListAsync());
      await dbLimpeza.SaveChangesAsync();
    }
  }
}

using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>`dbo.Setor.UtilizaKit` (spec da Fase 3B, secao 3) faz a volta completa, e o default do .sql e falso.</summary>
public class SetorUtilizaKitMapeamentoTests : TesteComBanco
{
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task UtilizaKit_faz_a_volta_completa(bool utilizaKit)
  {
    var nome = $"setor-kit-{Guid.NewGuid():N}"[..30];
    int id;
    await using (var db = NovoContexto())
    {
      var setor = new Setor { Nome = nome, Ativo = true, UtilizaKit = utilizaKit };
      db.Setores.Add(setor);
      await db.SaveChangesAsync();
      id = setor.Id;
    }
    try
    {
      await using var leitura = NovoContexto();
      Assert.Equal(utilizaKit, (await leitura.Setores.AsNoTracking().SingleAsync(s => s.Id == id)).UtilizaKit);
    }
    finally
    {
      await using var limpeza = NovoContexto();
      await limpeza.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Setor WHERE Id = {id}");
    }
  }

  [Fact]
  public async Task Insert_sem_a_coluna_grava_falso()
  {
    var nome = $"setor-kit-{Guid.NewGuid():N}"[..30];
    await using var db = NovoContexto();
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.Setor (Nome) VALUES ({nome})");
    try
    {
      Assert.False((await db.Setores.AsNoTracking().SingleAsync(s => s.Nome == nome)).UtilizaKit);
    }
    finally
    {
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Setor WHERE Nome = {nome}");
    }
  }
}

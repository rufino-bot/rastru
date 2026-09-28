using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>`dbo.Setor.Atividade` (spec da Fase 3D, secao 3.2) faz a volta completa, nula ou nao.</summary>
public class SetorAtividadeMapeamentoTests : TesteComBanco
{
  [Theory]
  [InlineData("montagem")]
  [InlineData(null)]
  public async Task Atividade_faz_a_volta_completa(string? atividade)
  {
    var nome = $"setor-atv-{Guid.NewGuid():N}"[..30];
    int id;
    await using (var db = NovoContexto())
    {
      var setor = new Setor { Nome = nome, Ativo = true, Atividade = atividade };
      db.Setores.Add(setor);
      await db.SaveChangesAsync();
      id = setor.Id;
    }
    try
    {
      await using var leitura = NovoContexto();
      Assert.Equal(atividade, (await leitura.Setores.AsNoTracking().SingleAsync(s => s.Id == id)).Atividade);
    }
    finally
    {
      await using var limpeza = NovoContexto();
      await limpeza.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Setor WHERE Id = {id}");
    }
  }
}

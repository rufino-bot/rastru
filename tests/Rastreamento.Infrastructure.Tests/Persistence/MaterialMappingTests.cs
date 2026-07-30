using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>Requer o SQL Server no ar (docker compose up -d) com o schema aplicado.</summary>
public class MaterialMappingTests : TesteComBanco
{
    [Fact]
    public async Task Mapeia_material_com_round_trip()
    {
        await using var db = NovoContexto();
        var material = new Material
        {
            Codigo = $"mat-{Guid.NewGuid():N}",
            Descricao = "Chapa de aco 3mm",
            UnidadeMedida = "KG",
            Ativo = true,
        };

        db.Materiais.Add(material);
        await db.SaveChangesAsync();
        var id = material.Id;

        try
        {
            await using var dbLeitura = NovoContexto();
            var carregado = await dbLeitura.Materiais.AsNoTracking().SingleAsync(m => m.Id == id);

            Assert.Equal(material.Codigo, carregado.Codigo);
            Assert.Equal("Chapa de aco 3mm", carregado.Descricao);
            Assert.Equal("KG", carregado.UnidadeMedida);
            Assert.True(carregado.Ativo);
        }
        finally
        {
            await using var dbLimpeza = NovoContexto();
            dbLimpeza.Materiais.RemoveRange(
                await dbLimpeza.Materiais.Where(m => m.Id == id).ToListAsync());
            await dbLimpeza.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Material_nasce_ativo_pelo_default_do_banco()
    {
        // INSERT cru omitindo `Ativo` de proposito: e o unico jeito de provar DF_Material_Ativo,
        // porque um INSERT feito pelo EF sempre manda a coluna (Database First: o default so vive
        // no .sql, MaterialConfiguration nao declara HasDefaultValue).
        await using var db = NovoContexto();
        var codigo = $"def-{Guid.NewGuid():N}";

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dbo.Material (Codigo, Descricao, UnidadeMedida) VALUES ({codigo}, 'Teste', 'UN')");

        var id = await db.Database
            .SqlQuery<int>($"SELECT Id AS [Value] FROM dbo.Material WHERE Codigo = {codigo}")
            .SingleAsync();

        try
        {
            await using var dbLeitura = NovoContexto();
            var carregado = await dbLeitura.Materiais.AsNoTracking().SingleAsync(m => m.Id == id);
            Assert.True(carregado.Ativo);
        }
        finally
        {
            await using var dbLimpeza = NovoContexto();
            dbLimpeza.Materiais.RemoveRange(
                await dbLimpeza.Materiais.Where(m => m.Id == id).ToListAsync());
            await dbLimpeza.SaveChangesAsync();
        }
    }
}

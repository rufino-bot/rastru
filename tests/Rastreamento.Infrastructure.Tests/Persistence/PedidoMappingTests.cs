using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Requer o SQL Server no ar (docker compose up -d) com o schema e o db/seed.sql aplicados —
/// e o unico lugar que prova as colunas de autoria da Task 1 contra o DDL de verdade.
/// </summary>
public class PedidoMappingTests : TesteComBanco
{
    /// <summary>FK_Pedido_CriadoPorUsuario nao aceita autor inventado: o Id sai do banco.</summary>
    private static async Task<int> IdDoAdmin(RastreamentoDbContext db) =>
        (await db.Usuarios.AsNoTracking().SingleAsync(u => u.NomeUsuario == "admin")).Id;

    [Fact]
    public async Task Mapeia_pedido_com_a_coluna_de_autoria()
    {
        await using var db = NovoContexto();
        var autor = await IdDoAdmin(db);
        var pedido = new Pedido
        {
            Numero = $"map-{Guid.NewGuid():N}"[..25],
            Cliente = "Cliente de teste",
            Tipo = "Fabricacao",
            Status = "Aberto",
            DataAbertura = DateTime.UtcNow,
            CriadoPorUsuarioId = autor,
        };

        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        var id = pedido.Id;

        try
        {
            await using var dbLeitura = NovoContexto();
            var carregado = await dbLeitura.Pedidos.AsNoTracking().SingleAsync(p => p.Id == id);

            Assert.Equal(pedido.Numero, carregado.Numero);
            Assert.Equal("Fabricacao", carregado.Tipo);
            Assert.Equal("Aberto", carregado.Status);
            Assert.Equal(autor, carregado.CriadoPorUsuarioId);
            Assert.Null(carregado.PedidoOrigemId);
            Assert.Null(carregado.MotivoRetrabalho);
            Assert.Null(carregado.DataConclusao);
        }
        finally
        {
            await using var dbLimpeza = NovoContexto();
            dbLimpeza.Pedidos.RemoveRange(await dbLimpeza.Pedidos.Where(p => p.Id == id).ToListAsync());
            await dbLimpeza.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Data_de_abertura_e_status_nascem_pelos_defaults_do_banco()
    {
        // INSERT cru omitindo Status e DataAbertura: e o unico jeito de provar DF_Pedido_Status e
        // DF_Pedido_DataAbertura, porque o EF sempre manda as colunas (Database First — os DEFAULT
        // vivem so no .sql, e o use case e quem define os valores no caminho normal).
        await using var db = NovoContexto();
        var autor = await IdDoAdmin(db);
        var numero = $"def-{Guid.NewGuid():N}"[..25];

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, CriadoPorUsuarioId) VALUES ({numero}, 'Teste', 'Fabricacao', {autor})");

        var id = await db.Database
            .SqlQuery<int>($"SELECT Id AS [Value] FROM dbo.Pedido WHERE Numero = {numero}").SingleAsync();

        try
        {
            await using var dbLeitura = NovoContexto();
            var carregado = await dbLeitura.Pedidos.AsNoTracking().SingleAsync(p => p.Id == id);

            Assert.Equal("Aberto", carregado.Status);
            // SYSUTCDATETIME(): a data do banco e UTC, nao o horario local do servidor.
            Assert.InRange(carregado.DataAbertura, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5));
        }
        finally
        {
            await using var dbLimpeza = NovoContexto();
            dbLimpeza.Pedidos.RemoveRange(await dbLimpeza.Pedidos.Where(p => p.Id == id).ToListAsync());
            await dbLimpeza.SaveChangesAsync();
        }
    }
}

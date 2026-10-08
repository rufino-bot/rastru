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
      DataEntrega = new DateOnly(2026, 10, 22),
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
  public async Task Data_de_entrega_vai_e_volta_da_coluna_date_sem_deslocar_o_dia()
  {
    // `DateOnly` contra `DATE`: o dia gravado e o dia lido. Um mapeamento por `DateTime`, com
    // conversao de fuso no caminho, devolveria o dia anterior — e e isso que este teste pega. O tipo e
    // a nulidade da coluna saem do catalogo do banco, nao do modelo do EF.
    await using var db = NovoContexto();
    var autor = await IdDoAdmin(db);
    var pedido = new Pedido
    {
      Numero = $"ent-{Guid.NewGuid():N}"[..25], Cliente = "Teste", Tipo = "Fabricacao", Status = "Aberto",
      DataAbertura = DateTime.UtcNow, DataEntrega = new DateOnly(2026, 10, 22), CriadoPorUsuarioId = autor,
    };
    db.Pedidos.Add(pedido);
    await db.SaveChangesAsync();

    try
    {
      await using var dbLeitura = NovoContexto();
      var lido = await dbLeitura.Pedidos.AsNoTracking().SingleAsync(p => p.Id == pedido.Id);
      Assert.Equal(new DateOnly(2026, 10, 22), lido.DataEntrega);

      var tipo = await dbLeitura.Database.SqlQuery<string>(
          $"SELECT DATA_TYPE AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Pedido' AND COLUMN_NAME = 'DataEntrega'")
          .SingleAsync();
      var anulavel = await dbLeitura.Database.SqlQuery<string>(
          $"SELECT IS_NULLABLE AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Pedido' AND COLUMN_NAME = 'DataEntrega'")
          .SingleAsync();
      Assert.Equal("date", tipo);
      Assert.Equal("NO", anulavel);
    }
    finally
    {
      await using var dbLimpeza = NovoContexto();
      await dbLimpeza.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Pedido WHERE Id = {pedido.Id}");
    }
  }

  [Fact]
  public async Task Data_de_abertura_e_status_nascem_pelos_defaults_do_banco()
  {
    // INSERT cru omitindo Status e DataAbertura: e o unico jeito de provar DF_Pedido_Status e
    // DF_Pedido_DataAbertura, porque o EF sempre manda as colunas (Database First — os DEFAULT
    // vivem so no .sql, e o use case e quem define os valores no caminho normal). `DataEntrega` vai no
    // `INSERT` porque nao tem `DEFAULT` (D2 da spec da data de entrega).
    await using var db = NovoContexto();
    var autor = await IdDoAdmin(db);
    var numero = $"def-{Guid.NewGuid():N}"[..25];

    await db.Database.ExecuteSqlInterpolatedAsync(
        $"INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, DataEntrega, CriadoPorUsuarioId) VALUES ({numero}, 'Teste', 'Fabricacao', '2026-10-22', {autor})");

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

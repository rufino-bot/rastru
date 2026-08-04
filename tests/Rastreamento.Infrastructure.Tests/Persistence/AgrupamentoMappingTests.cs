using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>Requer o SQL Server no ar (docker compose up -d) com o schema e o db/seed.sql aplicados.</summary>
public class AgrupamentoMappingTests : TesteComBanco
{
  /// <summary>Abre um Pedido real: FK_Agrupamento_Pedido nao aceita PedidoId inventado.</summary>
  private static async Task<(int PedidoId, int Autor)> NovoPedido(RastreamentoDbContext db)
  {
    var autor = (await db.Usuarios.AsNoTracking().SingleAsync(u => u.NomeUsuario == "admin")).Id;
    var pedido = new Pedido
    {
      Numero = $"agr-{Guid.NewGuid():N}"[..25],
      Cliente = "Cliente de teste",
      Tipo = "Fabricacao",
      Status = "Aberto",
      DataAbertura = DateTime.UtcNow,
      CriadoPorUsuarioId = autor,
    };
    db.Pedidos.Add(pedido);
    await db.SaveChangesAsync();
    return (pedido.Id, autor);
  }

  /// <summary>Agrupamento ANTES de Pedido — FK_Agrupamento_Pedido nao aceita a ordem inversa.</summary>
  private static async Task Limpar(int pedidoId)
  {
    await using var db = NovoContexto();
    db.Agrupamentos.RemoveRange(await db.Agrupamentos.Where(a => a.PedidoId == pedidoId).ToListAsync());
    await db.SaveChangesAsync();
    db.Pedidos.RemoveRange(await db.Pedidos.Where(p => p.Id == pedidoId).ToListAsync());
    await db.SaveChangesAsync();
  }

  [Fact]
  public async Task Mapeia_agrupamento_com_autoria()
  {
    await using var db = NovoContexto();
    var (pedidoId, autor) = await NovoPedido(db);

    try
    {
      var agrupamento = new Agrupamento
      {
        PedidoId = pedidoId,
        Codigo = "AG-01",
        Tipo = "Kit",
        CriadoPorUsuarioId = autor,
        CriadoEm = DateTime.UtcNow,
      };
      db.Agrupamentos.Add(agrupamento);
      await db.SaveChangesAsync();
      var id = agrupamento.Id;

      await using var dbLeitura = NovoContexto();
      var carregado = await dbLeitura.Agrupamentos.AsNoTracking().SingleAsync(a => a.Id == id);

      Assert.Equal(pedidoId, carregado.PedidoId);
      Assert.Equal("AG-01", carregado.Codigo);
      Assert.Equal("Kit", carregado.Tipo);
      Assert.Equal(autor, carregado.CriadoPorUsuarioId);
      Assert.Null(carregado.DataConclusao);
    }
    finally
    {
      await Limpar(pedidoId);
    }
  }

  [Fact]
  public async Task CriadoEm_nasce_pelo_default_do_banco()
  {
    // INSERT cru omitindo CriadoEm: e o unico jeito de provar DF_Agrupamento_CriadoEm, porque
    // o EF sempre manda a coluna no caminho normal.
    await using var db = NovoContexto();
    var (pedidoId, autor) = await NovoPedido(db);

    try
    {
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES ({pedidoId}, 'AG-DEF', 'Kit', {autor})");

      await using var dbLeitura = NovoContexto();
      var carregado = await dbLeitura.Agrupamentos.AsNoTracking()
          .SingleAsync(a => a.PedidoId == pedidoId && a.Codigo == "AG-DEF");

      // SYSUTCDATETIME(): UTC, nao o horario local do servidor.
      Assert.InRange(carregado.CriadoEm, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5));
    }
    finally
    {
      await Limpar(pedidoId);
    }
  }
}

using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class PedidoRepository : IPedidoRepository
{
  private readonly RastreamentoDbContext _db;

  public PedidoRepository(RastreamentoDbContext db) => _db = db;

  public Task<Pedido?> ObterPorIdAsync(int id, CancellationToken ct) =>
      _db.Pedidos.SingleOrDefaultAsync(p => p.Id == id, ct);

  public Task<Pedido?> ObterPorNumeroAsync(string numero, CancellationToken ct) =>
      _db.Pedidos.SingleOrDefaultAsync(p => p.Numero == numero, ct);

  public async Task<IReadOnlyList<Pedido>> ListarAsync(CancellationToken ct) =>
      await _db.Pedidos.AsNoTracking()
          .OrderByDescending(p => p.DataAbertura)
          .ToListAsync(ct);

  public async Task AdicionarAsync(Pedido pedido, CancellationToken ct) =>
      await _db.Pedidos.AddAsync(pedido, ct);

  public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}

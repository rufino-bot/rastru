using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class AgrupamentoRepository : IAgrupamentoRepository
{
  private readonly RastreamentoDbContext _db;

  public AgrupamentoRepository(RastreamentoDbContext db) => _db = db;

  public Task<Agrupamento?> ObterPorIdAsync(int id, CancellationToken ct) =>
      _db.Agrupamentos.SingleOrDefaultAsync(a => a.Id == id, ct);

  public Task<Agrupamento?> ObterPorPedidoECodigoAsync(
      int pedidoId, string codigo, CancellationToken ct) =>
      _db.Agrupamentos.SingleOrDefaultAsync(
          a => a.PedidoId == pedidoId && a.Codigo == codigo, ct);

  public async Task<IReadOnlyList<Agrupamento>> ListarPorPedidoAsync(
      int pedidoId, CancellationToken ct) =>
      await _db.Agrupamentos.AsNoTracking()
          .Where(a => a.PedidoId == pedidoId)
          .OrderBy(a => a.Codigo)
          .ToListAsync(ct);

  public async Task AdicionarAsync(Agrupamento agrupamento, CancellationToken ct) =>
      await _db.Agrupamentos.AddAsync(agrupamento, ct);

  public Task RemoverAsync(Agrupamento agrupamento, CancellationToken ct)
  {
    _db.Agrupamentos.Remove(agrupamento);
    return Task.CompletedTask;
  }

  public Task<bool> TemEstruturaAsync(int agrupamentoId, CancellationToken ct) =>
      _db.Estruturas.AnyAsync(e => e.AgrupamentoId == agrupamentoId, ct);

  public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}

using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Infrastructure.Persistence;

/// <summary>A consulta de pausa aberta, compartilhada por `ExecucaoRepository` e `PedidoRepository`.</summary>
internal static class PausasAbertas
{
  public static async Task<IReadOnlyDictionary<int, PausaAberta>> ListarAsync(
      RastreamentoDbContext db, IReadOnlyCollection<int> pedidoIds, CancellationToken ct)
  {
    if (pedidoIds.Count == 0) return new Dictionary<int, PausaAberta>();
    var lista = pedidoIds.ToList();
    return await (from pa in db.PedidoPausas.AsNoTracking()
                  join u in db.Usuarios.AsNoTracking() on pa.PausadoPorUsuarioId equals u.Id
                  where pa.RetomadoEm == null && lista.Contains(pa.PedidoId)
                  select new PausaAberta(pa.PedidoId, pa.PausadoEm, pa.PausadoPorUsuarioId, u.NomeCompleto, pa.Motivo))
        .ToDictionaryAsync(p => p.PedidoId, ct);
  }
}

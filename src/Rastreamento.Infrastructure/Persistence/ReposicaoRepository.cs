using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

/// <summary>
/// Ver o XML doc de <see cref="IReposicaoRepository"/> para o contrato. As leituras sao `AsNoTracking`, no molde de
/// <see cref="BaixaRepository"/>, e este repositorio tambem NAO tem `Salvar` proprio: o `DbContext` e o mesmo do
/// <see cref="ExecucaoRepository"/> (escopado).
/// </summary>
public class ReposicaoRepository : IReposicaoRepository
{
  private readonly RastreamentoDbContext _db;

  public ReposicaoRepository(RastreamentoDbContext db) => _db = db;

  public void Adicionar(Reclamacao reclamacao) => _db.Reclamacoes.Add(reclamacao);

  public Task<Reclamacao?> ObterReclamacaoAsync(int id, CancellationToken ct) =>
      _db.Reclamacoes.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);

  public async Task<IReadOnlyList<Reclamacao>> ListarReclamacoesAsync(int pedidoId, CancellationToken ct) =>
      await _db.Reclamacoes.AsNoTracking()
          .Where(r => r.PedidoId == pedidoId)
          .OrderByDescending(r => r.DataHora).ThenByDescending(r => r.Id)
          .ToListAsync(ct);

  public async Task<decimal> SomarReclamadoAsync(int pecaId, CancellationToken ct) =>
      await _db.Reclamacoes.AsNoTracking()
          .Where(r => r.EstruturaItemId == pecaId && r.EstornadaEm == null)
          .SumAsync(r => (decimal?)r.Quantidade, ct) ?? 0m;

  public async Task<bool> MarcarReclamacaoEstornadaAsync(int id, int usuarioId, DateTime em, CancellationToken ct) =>
      await _db.Reclamacoes.Where(r => r.Id == id && r.EstornadaEm == null)
          .ExecuteUpdateAsync(s => s
              .SetProperty(r => r.EstornadaEm, (DateTime?)em)
              .SetProperty(r => r.EstornadaPorUsuarioId, (int?)usuarioId), ct) > 0;

  public Task<bool> PedidoExisteAsync(int pedidoId, CancellationToken ct) =>
      _db.Pedidos.AsNoTracking().AnyAsync(p => p.Id == pedidoId, ct);
}

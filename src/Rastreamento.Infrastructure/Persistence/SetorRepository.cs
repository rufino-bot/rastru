using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class SetorRepository : ISetorRepository
{
  private readonly RastreamentoDbContext _db;

  public SetorRepository(RastreamentoDbContext db) => _db = db;

  // Sem AsNoTracking de proposito nos dois Obter*: ver o contrato da interface (Editar e
  // DefinirAtivo mutam a entidade devolvida e dependem do change tracking).
  public Task<Setor?> ObterPorIdAsync(int id, CancellationToken ct) =>
      _db.Setores.SingleOrDefaultAsync(s => s.Id == id, ct);

  public Task<Setor?> ObterPorNomeAsync(string nome, CancellationToken ct) =>
      _db.Setores.SingleOrDefaultAsync(s => s.Nome == nome, ct);

  public async Task<IReadOnlyList<Setor>> ListarAsync(bool incluirInativos, CancellationToken ct) =>
      await _db.Setores.AsNoTracking()
          .Where(s => incluirInativos || s.Ativo)
          .OrderBy(s => s.Nome)
          .ToListAsync(ct);

  public async Task AdicionarAsync(Setor setor, CancellationToken ct) =>
      await _db.Setores.AddAsync(setor, ct);

  public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}

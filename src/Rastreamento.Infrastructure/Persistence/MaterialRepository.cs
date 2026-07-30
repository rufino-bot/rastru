using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class MaterialRepository : IMaterialRepository
{
    private readonly RastreamentoDbContext _db;

    public MaterialRepository(RastreamentoDbContext db) => _db = db;

    // Sem AsNoTracking de proposito nos dois Obter*: ver o contrato da interface (Editar e
    // DefinirAtivo mutam a entidade devolvida e dependem do change tracking).
    public Task<Material?> ObterPorIdAsync(int id, CancellationToken ct) =>
        _db.Materiais.SingleOrDefaultAsync(m => m.Id == id, ct);

    public Task<Material?> ObterPorCodigoAsync(string codigo, CancellationToken ct) =>
        _db.Materiais.SingleOrDefaultAsync(m => m.Codigo == codigo, ct);

    public async Task<IReadOnlyList<Material>> ListarAsync(bool incluirInativos, CancellationToken ct) =>
        await _db.Materiais.AsNoTracking()
            .Where(m => incluirInativos || m.Ativo)
            .OrderBy(m => m.Codigo)
            .ToListAsync(ct);

    public async Task AdicionarAsync(Material material, CancellationToken ct) =>
        await _db.Materiais.AddAsync(material, ct);

    public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}

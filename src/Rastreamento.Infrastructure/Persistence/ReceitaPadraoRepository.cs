using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class ReceitaPadraoRepository : IReceitaPadraoRepository
{
  private readonly RastreamentoDbContext _db;

  public ReceitaPadraoRepository(RastreamentoDbContext db) => _db = db;

  public Task<Componente?> ObterComponenteAsync(int id, CancellationToken ct) =>
      _db.Componentes.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);

  public async Task<IReadOnlyList<ComponenteFilhoPadrao>> ListarFilhosAsync(
      int componenteId, CancellationToken ct) =>
      await _db.FilhosPadrao.AsNoTracking()
          .Where(f => f.ComponentePaiId == componenteId)
          .OrderBy(f => f.Id)
          .ToListAsync(ct);

  public async Task<IReadOnlyList<ComponenteMaterialPadrao>> ListarMateriaisAsync(
      int componenteId, CancellationToken ct) =>
      await _db.MateriaisPadrao.AsNoTracking()
          .Where(m => m.ComponenteId == componenteId)
          .OrderBy(m => m.Id)
          .ToListAsync(ct);

  // OrderBy(Ordem) e contrato, nao conveniencia: a tela desenha a sequencia na ordem que chega.
  public async Task<IReadOnlyList<ComponenteRoteiroPadrao>> ListarRoteiroAsync(
      int componenteId, CancellationToken ct) =>
      await _db.RoteirosPadrao.AsNoTracking()
          .Where(r => r.ComponenteId == componenteId)
          .OrderBy(r => r.Ordem)
          .ToListAsync(ct);

  public async Task<IReadOnlyList<Componente>> ObterComponentesPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct) =>
      await _db.Componentes.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(ct);

  public async Task<IReadOnlyList<Material>> ObterMateriaisPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct) =>
      await _db.Materiais.AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync(ct);

  public async Task<IReadOnlyList<Setor>> ObterSetoresPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct) =>
      await _db.Setores.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(ct);

  public async Task<IReadOnlyList<ComponenteFilhoPadrao>> ListarTodasAsArestasAsync(
      CancellationToken ct) =>
      await _db.FilhosPadrao.AsNoTracking().ToListAsync(ct);

  public Task SubstituirFilhosAsync(
      int componenteId, IReadOnlyList<ComponenteFilhoPadrao> novas, CancellationToken ct) =>
      Substituir(_db.FilhosPadrao, f => f.ComponentePaiId == componenteId, novas, ct);

  public Task SubstituirMateriaisAsync(
      int componenteId, IReadOnlyList<ComponenteMaterialPadrao> novas, CancellationToken ct) =>
      Substituir(_db.MateriaisPadrao, m => m.ComponenteId == componenteId, novas, ct);

  public Task SubstituirRoteiroAsync(
      int componenteId, IReadOnlyList<ComponenteRoteiroPadrao> novas, CancellationToken ct) =>
      Substituir(_db.RoteirosPadrao, r => r.ComponenteId == componenteId, novas, ct);

  /// <summary>
  /// Remove + adiciona + UM SaveChanges. O EF envolve o SaveChanges numa transacao sozinho,
  /// entao "apagou e nao gravou" nao e estado alcancavel — e essa e a propriedade que o teste
  /// `Substituir_filhos_apaga_as_linhas_antigas_e_grava_as_novas` protege.
  ///
  /// Nao usa ExecuteDeleteAsync: ele emite um DELETE FORA da transacao do SaveChanges, o que
  /// reabriria exatamente o meio-termo que este desenho fecha.
  /// </summary>
  private async Task Substituir<T>(
      DbSet<T> tabela,
      Expression<Func<T, bool>> doComponente,
      IReadOnlyList<T> novas,
      CancellationToken ct) where T : class
  {
    var antigas = await tabela.Where(doComponente).ToListAsync(ct);
    tabela.RemoveRange(antigas);
    tabela.AddRange(novas);
    await _db.SaveChangesAsync(ct);
  }
}

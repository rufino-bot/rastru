using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

/// <summary>
/// Ver o XML doc de <see cref="ISeparacaoRepository"/> para o contrato. As leituras sao `AsNoTracking`
/// e com `Contains`, no molde de `ExecucaoRepository`: uma consulta para todos os nos pedidos, e nao
/// uma por no.
/// </summary>
public class SeparacaoRepository : ISeparacaoRepository
{
  private readonly RastreamentoDbContext _db;

  public SeparacaoRepository(RastreamentoDbContext db) => _db = db;

  public Task<bool> NoExisteAsync(int estruturaItemId, CancellationToken ct) =>
      _db.Estruturas.AsNoTracking().AnyAsync(e => e.Id == estruturaItemId, ct);

  public async Task<IReadOnlyList<EstruturaMaterial>> ListarPlanejadoAsync(
      IReadOnlyCollection<int> estruturaItemIds, CancellationToken ct)
  {
    if (estruturaItemIds.Count == 0) return [];
    var ids = estruturaItemIds.ToList();
    return await _db.EstruturaMateriais.AsNoTracking()
        .Where(m => ids.Contains(m.EstruturaItemId))
        .OrderBy(m => m.Id)
        .ToListAsync(ct);
  }

  public async Task<IReadOnlyList<MaterialSeparacao>> ListarLinhasAsync(
      IReadOnlyCollection<int> estruturaItemIds, CancellationToken ct)
  {
    if (estruturaItemIds.Count == 0) return [];
    var ids = estruturaItemIds.ToList();
    return await _db.Separacoes.AsNoTracking()
        .Where(s => ids.Contains(s.EstruturaItemId))
        .OrderBy(s => s.Id)
        .ToListAsync(ct);
  }

  public Task<MaterialSeparacao?> ObterLinhaAsync(int id, CancellationToken ct) =>
      _db.Separacoes.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);

  public Task<bool> FoiEstornadaAsync(int separacaoId, CancellationToken ct) =>
      _db.Separacoes.AsNoTracking().AnyAsync(s => s.EstornoDeId == separacaoId, ct);

  public async Task<IReadOnlyList<MaterialDaSeparacao>> ListarMateriaisAsync(
      IReadOnlyCollection<int> materialIds, CancellationToken ct)
  {
    if (materialIds.Count == 0) return [];
    var ids = materialIds.ToList();
    // LEFT JOIN: o Setor de estoque e opcional, e o inativo conta (o nome continua sendo o de onde fica).
    return await (from m in _db.Materiais.AsNoTracking()
                  join s in _db.Setores.AsNoTracking() on m.SetorDeEstoqueId equals s.Id into setores
                  from s in setores.DefaultIfEmpty()
                  where ids.Contains(m.Id)
                  orderby m.Id
                  select new MaterialDaSeparacao(
                      m.Id, m.Codigo, m.Descricao, m.UnidadeMedida, m.Ativo, m.SetorDeEstoqueId, s == null ? null : s.Nome))
        .ToListAsync(ct);
  }

  public async Task<IReadOnlySet<int>> ListarNosComMaterialDoAlmoxarifadoAsync(
      IReadOnlyCollection<int> estruturaItemIds, CancellationToken ct)
  {
    if (estruturaItemIds.Count == 0) return new HashSet<int>();
    var ids = estruturaItemIds.ToList();
    var nos = await (from em in _db.EstruturaMateriais.AsNoTracking()
                     join m in _db.Materiais.AsNoTracking() on em.MaterialId equals m.Id
                     where ids.Contains(em.EstruturaItemId) && m.SetorDeEstoqueId == null
                     select em.EstruturaItemId)
        .Distinct()
        .ToListAsync(ct);
    return nos.ToHashSet();
  }

  public async Task SubstituirPlanejadoAsync(
      int estruturaItemId, IReadOnlyList<(int MaterialId, decimal Quantidade)> linhas, CancellationToken ct)
  {
    var atuais = await _db.EstruturaMateriais.Where(m => m.EstruturaItemId == estruturaItemId).ToListAsync(ct);
    _db.EstruturaMateriais.RemoveRange(atuais);
    foreach (var (materialId, quantidade) in linhas)
      _db.EstruturaMateriais.Add(new EstruturaMaterial
      {
        EstruturaItemId = estruturaItemId, MaterialId = materialId, Quantidade = quantidade,
      });
    await _db.SaveChangesAsync(ct);
  }

  public async Task GravarAsync(IReadOnlyList<MaterialSeparacao> linhas, CancellationToken ct)
  {
    if (linhas.Count == 0) return;
    _db.Separacoes.AddRange(linhas);
    await _db.SaveChangesAsync(ct);
  }

  public async Task<bool> GravarEstornoAsync(MaterialSeparacao estorno, CancellationToken ct)
  {
    _db.Separacoes.Add(estorno);
    try
    {
      await _db.SaveChangesAsync(ct);
      return true;
    }
    catch (DbUpdateException e) when (ErrosDoSqlServer.EhViolacaoDeUnicidade(e))
    {
      // Outro estorno da mesma linha chegou antes (UX_MaterialSeparacao_EstornoDe). A entidade fica
      // desanexada para o contexto nao tentar grava-la de novo num SaveChanges seguinte.
      _db.Entry(estorno).State = EntityState.Detached;
      return false;
    }
  }
}

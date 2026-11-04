using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

/// <summary>
/// Ver o XML doc de <see cref="IBaixaRepository"/> para o contrato. As leituras sao `AsNoTracking` e com `Contains`,
/// no molde de <see cref="ExpedicaoRepository"/>. Este repositorio NAO tem `Salvar` proprio: o `DbContext` e o mesmo do
/// <see cref="ExecucaoRepository"/> (escopado), e quem persiste e o `SalvarAlteracoesAsync` dele, na transacao da
/// execucao — a `Perda` nasce junto das linhas do livro que a apontam.
/// </summary>
public class BaixaRepository : IBaixaRepository
{
  private readonly RastreamentoDbContext _db;

  public BaixaRepository(RastreamentoDbContext db) => _db = db;

  public void Adicionar(Perda perda) => _db.Perdas.Add(perda);

  public Task<Perda?> ObterAsync(int id, CancellationToken ct) =>
      _db.Perdas.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);

  public async Task<IReadOnlyList<Movimentacao>> ListarLinhasDasPerdasAsync(
      IReadOnlyCollection<int> perdaIds, CancellationToken ct)
  {
    if (perdaIds.Count == 0) return [];
    var ids = perdaIds.ToList();
    return await _db.Movimentacoes.AsNoTracking()
        .Where(m => m.PerdaId != null && ids.Contains(m.PerdaId!.Value))
        .OrderBy(m => m.Id)
        .ToListAsync(ct);
  }

  /// <summary>Por nivel, uma consulta por nivel, como `ExecucaoRepository.ListarIdsDaSubarvoreAsync`.</summary>
  public async Task<IReadOnlyList<int>> ListarSubarvoreAsync(int noId, CancellationToken ct)
  {
    var ids = new List<int>();
    var fronteira = await _db.Estruturas.AsNoTracking().Where(e => e.Id == noId).Select(e => e.Id).ToListAsync(ct);
    var visitados = new HashSet<int>();
    while (fronteira.Count > 0)
    {
      // Mesma defesa de `EstruturaRepository.RemoverSubarvoreAsync` contra ciclo nos dados.
      foreach (var visto in fronteira)
        if (!visitados.Add(visto))
          throw new SubarvoreCiclicaException(
              $"A subarvore a partir do no {noId} tem um ciclo em EstruturaPaiId: o no {visto} reaparece.");
      ids.AddRange(fronteira);
      var atual = fronteira;
      fronteira = await _db.Estruturas.AsNoTracking()
          .Where(e => e.EstruturaPaiId != null && atual.Contains(e.EstruturaPaiId!.Value))
          .Select(e => e.Id)
          .ToListAsync(ct);
    }
    return ids.Order().ToList();
  }

  /// <summary>Sobe um nivel por consulta: a arvore e rasa, e um ciclo nos dados para com a mesma excecao da subarvore.</summary>
  public async Task<IReadOnlyList<int>> ListarCadeiaAteOTopoAsync(int noId, CancellationToken ct)
  {
    var cadeia = new List<int>();
    var visitados = new HashSet<int> { noId };
    var pai = await PaiAsync(noId, ct);
    while (pai is int id)
    {
      if (!visitados.Add(id))
        throw new SubarvoreCiclicaException(
            $"A cadeia a partir do no {noId} tem um ciclo em EstruturaPaiId: o no {id} reaparece.");
      cadeia.Add(id);
      pai = await PaiAsync(id, ct);
    }
    return cadeia;
  }

  private Task<int?> PaiAsync(int id, CancellationToken ct) =>
      _db.Estruturas.AsNoTracking().Where(e => e.Id == id).Select(e => e.EstruturaPaiId).SingleOrDefaultAsync(ct);

  public async Task<IReadOnlyDictionary<int, string>> ListarStatusDosPedidosAsync(
      IReadOnlyCollection<int> pedidoIds, CancellationToken ct)
  {
    if (pedidoIds.Count == 0) return new Dictionary<int, string>();
    var ids = pedidoIds.ToList();
    return await _db.Pedidos.AsNoTracking()
        .Where(p => ids.Contains(p.Id))
        .ToDictionaryAsync(p => p.Id, p => p.Status, ct);
  }

  public async Task<IReadOnlyList<int>> ListarPecasDoEscopoAsync(int pedidoId, int? agrupamentoId, CancellationToken ct) =>
      await (from e in _db.Estruturas.AsNoTracking()
             join a in _db.Agrupamentos.AsNoTracking() on e.AgrupamentoId equals a.Id
             where e.EstruturaPaiId == null && a.PedidoId == pedidoId && (agrupamentoId == null || a.Id == agrupamentoId)
             orderby e.Id
             select e.Id)
          .ToListAsync(ct);

  public Task<int?> ObterPedidoDoAgrupamentoAsync(int agrupamentoId, CancellationToken ct) =>
      _db.Agrupamentos.AsNoTracking().Where(a => a.Id == agrupamentoId).Select(a => (int?)a.PedidoId).SingleOrDefaultAsync(ct);
}

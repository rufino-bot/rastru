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

  public async Task<IReadOnlyList<Reclamacao>> ListarReclamacoesVivasDaPecaAsync(int pecaId, CancellationToken ct) =>
      await _db.Reclamacoes.AsNoTracking()
          .Where(r => r.EstruturaItemId == pecaId && r.EstornadaEm == null)
          .OrderBy(r => r.Id)
          .ToListAsync(ct);

  public async Task<bool> MarcarReclamacaoEstornadaAsync(int id, int usuarioId, DateTime em, CancellationToken ct) =>
      await _db.Reclamacoes.Where(r => r.Id == id && r.EstornadaEm == null)
          .ExecuteUpdateAsync(s => s
              .SetProperty(r => r.EstornadaEm, (DateTime?)em)
              .SetProperty(r => r.EstornadaPorUsuarioId, (int?)usuarioId), ct) > 0;

  public Task<bool> PedidoExisteAsync(int pedidoId, CancellationToken ct) =>
      _db.Pedidos.AsNoTracking().AnyAsync(p => p.Id == pedidoId, ct);

  public async Task<IReadOnlyDictionary<int, string>> ListarNumerosDePedidosAsync(
      IReadOnlyCollection<int> pedidoIds, CancellationToken ct) =>
      pedidoIds.Count == 0
          ? new Dictionary<int, string>()
          : await _db.Pedidos.AsNoTracking().Where(p => pedidoIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Numero, ct);

  public async Task<IReadOnlyList<Perda>> ListarPerdasSemRetrabalhoAsync(IReadOnlyCollection<int> pedidoIds, CancellationToken ct)
  {
    if (pedidoIds.Count == 0) return [];
    var repoem = MotivosDePerda.QueSeRepoem;
    return await _db.Perdas.AsNoTracking()
        .Where(p => pedidoIds.Contains(p.PedidoId) && p.PedidoRetrabalhoId == null && repoem.Contains(p.Motivo))
        .ToListAsync(ct);
  }

  public async Task<IReadOnlyList<Reclamacao>> ListarReclamacoesSemRetrabalhoAsync(
      IReadOnlyCollection<int> pedidoIds, CancellationToken ct)
  {
    if (pedidoIds.Count == 0) return [];
    return await _db.Reclamacoes.AsNoTracking()
        .Where(r => pedidoIds.Contains(r.PedidoId) && r.PedidoRetrabalhoId == null && r.EstornadaEm == null)
        .ToListAsync(ct);
  }

  public void Adicionar(DispensaDeReposicao dispensa) => _db.DispensasDeReposicao.Add(dispensa);

  public Task<DispensaDeReposicao?> ObterDispensaAsync(int id, CancellationToken ct) =>
      _db.DispensasDeReposicao.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);

  public async Task<IReadOnlyList<DispensaDeReposicao>> ListarDispensasVigentesAsync(
      IReadOnlyCollection<int> perdaIds, IReadOnlyCollection<int> reclamacaoIds, CancellationToken ct)
  {
    if (perdaIds.Count == 0 && reclamacaoIds.Count == 0) return [];
    return await _db.DispensasDeReposicao.AsNoTracking()
        .Where(d => d.EstornadaEm == null
            && ((d.PerdaId != null && perdaIds.Contains(d.PerdaId.Value))
                || (d.ReclamacaoId != null && reclamacaoIds.Contains(d.ReclamacaoId.Value))))
        .ToListAsync(ct);
  }

  public async Task<bool> MarcarDispensaEstornadaAsync(int id, int usuarioId, DateTime em, CancellationToken ct) =>
      await _db.DispensasDeReposicao.Where(d => d.Id == id && d.EstornadaEm == null)
          .ExecuteUpdateAsync(s => s
              .SetProperty(d => d.EstornadaEm, (DateTime?)em)
              .SetProperty(d => d.EstornadaPorUsuarioId, (int?)usuarioId), ct) > 0;

  public async Task<bool> MarcarPerdaRepostaAsync(int perdaId, int pedidoRetrabalhoId, CancellationToken ct) =>
      await _db.Perdas.Where(p => p.Id == perdaId && p.PedidoRetrabalhoId == null)
          .ExecuteUpdateAsync(s => s.SetProperty(p => p.PedidoRetrabalhoId, (int?)pedidoRetrabalhoId), ct) > 0;

  public async Task<bool> MarcarReclamacaoRepostaAsync(int reclamacaoId, int pedidoRetrabalhoId, CancellationToken ct) =>
      await _db.Reclamacoes.Where(r => r.Id == reclamacaoId && r.PedidoRetrabalhoId == null && r.EstornadaEm == null)
          .ExecuteUpdateAsync(s => s.SetProperty(r => r.PedidoRetrabalhoId, (int?)pedidoRetrabalhoId), ct) > 0;

  public async Task<IReadOnlyList<RetrabalhoDoPedido>> ListarRetrabalhosAsync(int pedidoOrigemId, CancellationToken ct)
  {
    var retrabalhos = await _db.Pedidos.AsNoTracking()
        .Where(p => p.PedidoOrigemId == pedidoOrigemId)
        .OrderBy(p => p.DataAbertura).ThenBy(p => p.Id)
        .ToListAsync(ct);
    if (retrabalhos.Count == 0) return [];
    var ids = retrabalhos.Select(p => p.Id).ToList();

    // So as baixas de perda repoem: a `Reaproveitado` tambem aponta o Retrabalho, mas como destino da sobra.
    var repostas = MotivosDePerda.QueSeRepoem;
    var perdas = await _db.Perdas.AsNoTracking()
        .Where(p => p.PedidoRetrabalhoId != null && ids.Contains(p.PedidoRetrabalhoId.Value) && repostas.Contains(p.Motivo))
        .Select(p => new { p.Id, Retrabalho = p.PedidoRetrabalhoId!.Value })
        .ToListAsync(ct);
    var reclamacoes = await _db.Reclamacoes.AsNoTracking()
        .Where(r => r.PedidoRetrabalhoId != null && ids.Contains(r.PedidoRetrabalhoId.Value))
        .Select(r => new { r.Id, Retrabalho = r.PedidoRetrabalhoId!.Value })
        .ToListAsync(ct);

    return retrabalhos.Select(p => new RetrabalhoDoPedido(
        p,
        perdas.FirstOrDefault(x => x.Retrabalho == p.Id)?.Id,
        reclamacoes.FirstOrDefault(x => x.Retrabalho == p.Id)?.Id)).ToList();
  }
}

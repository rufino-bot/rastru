using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

/// <summary>
/// Ver o XML doc de <see cref="IExpedicaoRepository"/> para o contrato. As leituras sao `AsNoTracking` e com
/// `Contains`, no molde de <see cref="SeparacaoRepository"/>: uma consulta para todas as Pecas pedidas, e nao
/// uma por Peca. Este repositorio NAO tem `Salvar` proprio: o `DbContext` e o mesmo do
/// <see cref="ExecucaoRepository"/> (escopado), e quem persiste e o `SalvarAlteracoesAsync` dele, na transacao
/// da execucao — a afericao, a contagem, a liberacao e a remessa nascem junto das linhas do livro.
/// </summary>
public class ExpedicaoRepository : IExpedicaoRepository
{
  private const string StatusConcluido = "Concluido";
  private const string StatusCancelado = "Cancelado";

  private readonly RastreamentoDbContext _db;

  public ExpedicaoRepository(RastreamentoDbContext db) => _db = db;

  public void Adicionar(RelatorioDimensional afericao) => _db.Afericoes.Add(afericao);

  public void Adicionar(ContagemDimensional contagem) => _db.Contagens.Add(contagem);

  public void Adicionar(LiberacaoDimensional liberacao) => _db.Liberacoes.Add(liberacao);

  public void Adicionar(Remessa remessa) => _db.Remessas.Add(remessa);

  public async Task<IReadOnlyList<RelatorioDimensional>> ListarAfericoesAsync(
      IReadOnlyCollection<int> pecaIds, CancellationToken ct)
  {
    if (pecaIds.Count == 0) return [];
    var ids = pecaIds.ToList();
    return await _db.Afericoes.AsNoTracking()
        .Where(a => ids.Contains(a.EstruturaItemId))
        .OrderBy(a => a.Id)
        .ToListAsync(ct);
  }

  public Task<RelatorioDimensional?> ObterAfericaoAsync(int id, CancellationToken ct) =>
      _db.Afericoes.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);

  public async Task<IReadOnlyList<ContagemDimensional>> ListarContagensAsync(
      IReadOnlyCollection<int> pecaIds, CancellationToken ct)
  {
    if (pecaIds.Count == 0) return [];
    var ids = pecaIds.ToList();
    return await _db.Contagens.AsNoTracking()
        .Where(c => ids.Contains(c.EstruturaItemId))
        .OrderBy(c => c.Id)
        .ToListAsync(ct);
  }

  public Task<ContagemDimensional?> ObterContagemAsync(int id, CancellationToken ct) =>
      _db.Contagens.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);

  public async Task<IReadOnlyList<LiberacaoDimensional>> ListarLiberacoesAsync(
      IReadOnlyCollection<int> pecaIds, CancellationToken ct)
  {
    if (pecaIds.Count == 0) return [];
    var ids = pecaIds.ToList();
    return await _db.Liberacoes.AsNoTracking()
        .Where(l => ids.Contains(l.EstruturaItemId))
        .OrderBy(l => l.Id)
        .ToListAsync(ct);
  }

  public async Task<IReadOnlyList<LiberacaoDimensional>> ListarPedidosDeLiberacaoAsync(CancellationToken ct) =>
      await (from l in _db.Liberacoes.AsNoTracking()
             join e in _db.Estruturas.AsNoTracking() on l.EstruturaItemId equals e.Id
             join a in _db.Agrupamentos.AsNoTracking() on e.AgrupamentoId equals a.Id
             join p in _db.Pedidos.AsNoTracking() on a.PedidoId equals p.Id
             where l.Tipo == TiposDeLiberacao.Pedido && p.Status != StatusConcluido && p.Status != StatusCancelado
             orderby l.Id
             select l)
          .ToListAsync(ct);

  public Task<LiberacaoDimensional?> ObterLiberacaoAsync(int id, CancellationToken ct) =>
      _db.Liberacoes.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, ct);

  public Task<Remessa?> ObterRemessaAsync(int id, CancellationToken ct) =>
      _db.Remessas.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);

  public async Task<IReadOnlyList<Remessa>> ListarRemessasDesdeAsync(DateTime desdeUtc, CancellationToken ct) =>
      await _db.Remessas.AsNoTracking()
          .Where(r => r.DataHora >= desdeUtc)
          .OrderByDescending(r => r.DataHora).ThenByDescending(r => r.Id)
          .ToListAsync(ct);

  public async Task<IReadOnlyList<Movimentacao>> ListarLinhasDasContagensAsync(
      IReadOnlyCollection<int> contagemIds, CancellationToken ct)
  {
    if (contagemIds.Count == 0) return [];
    var ids = contagemIds.ToList();
    return await _db.Movimentacoes.AsNoTracking()
        .Where(m => m.ContagemId != null && ids.Contains(m.ContagemId!.Value))
        .OrderBy(m => m.Id)
        .ToListAsync(ct);
  }

  public async Task<IReadOnlyList<Movimentacao>> ListarLinhasDasLiberacoesAsync(
      IReadOnlyCollection<int> liberacaoIds, CancellationToken ct)
  {
    if (liberacaoIds.Count == 0) return [];
    var ids = liberacaoIds.ToList();
    return await _db.Movimentacoes.AsNoTracking()
        .Where(m => m.LiberacaoId != null && ids.Contains(m.LiberacaoId!.Value))
        .OrderBy(m => m.Id)
        .ToListAsync(ct);
  }

  public async Task<IReadOnlyList<Movimentacao>> ListarLinhasDasRemessasAsync(
      IReadOnlyCollection<int> remessaIds, CancellationToken ct)
  {
    if (remessaIds.Count == 0) return [];
    var ids = remessaIds.ToList();
    return await _db.Movimentacoes.AsNoTracking()
        .Where(m => m.RemessaId != null && ids.Contains(m.RemessaId!.Value))
        .OrderBy(m => m.Id)
        .ToListAsync(ct);
  }

  public async Task<IReadOnlyDictionary<int, PedidoDaPeca>> ListarPedidosDasPecasAsync(
      IReadOnlyCollection<int> pecaIds, CancellationToken ct)
  {
    if (pecaIds.Count == 0) return new Dictionary<int, PedidoDaPeca>();
    var ids = pecaIds.ToList();
    var linhas = await (from e in _db.Estruturas.AsNoTracking()
                        join a in _db.Agrupamentos.AsNoTracking() on e.AgrupamentoId equals a.Id
                        join p in _db.Pedidos.AsNoTracking() on a.PedidoId equals p.Id
                        where ids.Contains(e.Id)
                        select new { e.Id, PedidoId = p.Id, p.Numero, p.Status })
        .ToListAsync(ct);
    return linhas.ToDictionary(l => l.Id, l => new PedidoDaPeca(l.PedidoId, l.Numero, l.Status));
  }

  public async Task<IReadOnlySet<int>> ListarAfericoesEstornadasAsync(
      IReadOnlyCollection<int> afericaoIds, CancellationToken ct)
  {
    if (afericaoIds.Count == 0) return new HashSet<int>();
    var ids = afericaoIds.ToList();
    var estornadas = await _db.Afericoes.AsNoTracking()
        .Where(a => a.EstornoDeId != null && ids.Contains(a.EstornoDeId!.Value))
        .Select(a => a.EstornoDeId!.Value)
        .ToListAsync(ct);
    return estornadas.ToHashSet();
  }
}

using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

/// <summary>
/// Ver o XML doc de <see cref="IEmbalagemRepository"/> para o contrato. A leitura e uma ida ao banco so para
/// todas as Pecas pedidas: a ultima linha de cada uma e o `EXISTS` sobre o livro vao na mesma consulta. A tabela
/// e so de inclusao; este repositorio so adiciona.
/// </summary>
public class EmbalagemRepository : IEmbalagemRepository
{
  private readonly RastreamentoDbContext _db;

  public EmbalagemRepository(RastreamentoDbContext db) => _db = db;

  public async Task<IReadOnlyList<UltimaEmbalagem>> ListarUltimasAsync(
      IReadOnlyCollection<int> pecaIds, IReadOnlyCollection<string> posicoesDeProducao, CancellationToken ct)
  {
    if (pecaIds.Count == 0) return [];
    var ids = pecaIds.ToList();
    var posicoes = posicoesDeProducao.ToList();

    return await _db.Embalagens.AsNoTracking()
        .Where(e => ids.Contains(e.EstruturaItemId)
            && e.Id == _db.Embalagens.Where(o => o.EstruturaItemId == e.EstruturaItemId).Max(o => o.Id))
        .Select(e => new UltimaEmbalagem(
            e.EstruturaItemId, e.Tipo, e.DataHora, e.UsuarioId,
            _db.Movimentacoes.Any(m => m.EstruturaItemId == e.EstruturaItemId
                && m.DataHora > e.DataHora && posicoes.Contains(m.DestinoPosicao))))
        .ToListAsync(ct);
  }

  public async Task GravarAsync(Embalagem linha, CancellationToken ct)
  {
    _db.Embalagens.Add(linha);
    await _db.SaveChangesAsync(ct);
  }
}

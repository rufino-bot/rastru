using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class ArquivoDeComponenteRepository : IArquivoDeComponenteRepository
{
  private readonly RastreamentoDbContext _db;

  public ArquivoDeComponenteRepository(RastreamentoDbContext db) => _db = db;

  public async Task<int> GravarEVincularComoSolidoAsync(
      int componenteId, ArquivoDeComponente arquivo, CancellationToken ct)
  {
    await using var transacao = await _db.Database.BeginTransactionAsync(ct);

    _db.ArquivosDeComponente.Add(arquivo);
    await _db.SaveChangesAsync(ct);

    var componente = await _db.Componentes.SingleAsync(c => c.Id == componenteId, ct);
    componente.ArquivoSolidoId = arquivo.Id;
    await _db.SaveChangesAsync(ct);

    await transacao.CommitAsync(ct);
    return arquivo.Id;
  }

  public async Task<ArquivoDeComponente?> ObterSolidoDoComponenteAsync(
      int componenteId, CancellationToken ct)
  {
    // Duas consultas em vez de um JOIN com navegacao: a navegacao e justamente o que nao existe,
    // por desenho. O primeiro SELECT le SO o id — nao toca no blob.
    var arquivoId = await _db.Componentes.AsNoTracking()
        .Where(c => c.Id == componenteId)
        .Select(c => c.ArquivoSolidoId)
        .SingleOrDefaultAsync(ct);

    if (arquivoId is null) return null;

    return await _db.ArquivosDeComponente.AsNoTracking()
        .SingleOrDefaultAsync(a => a.Id == arquivoId.Value, ct);
  }
}

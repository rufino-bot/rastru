using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly RastreamentoDbContext _db;

    public RefreshTokenRepository(RastreamentoDbContext db) => _db = db;

    public async Task AdicionarAsync(RefreshToken token, CancellationToken ct) =>
        await _db.RefreshTokens.AddAsync(token, ct);

    /// <summary>
    /// Filtra <c>RevogadoEm IS NULL</c> conforme o contrato de <see cref="IRefreshTokenRepository"/>;
    /// a expiracao fica por conta do caso de uso. O token volta rastreado de proposito: a rotacao
    /// muta o registro atual e conta com o change tracking para revoga-lo no mesmo
    /// <c>SaveChanges</c> que insere o novo.
    /// </summary>
    public Task<RefreshToken?> ObterAtivoPorHashAsync(string tokenHash, CancellationToken ct) =>
        _db.RefreshTokens
            .Include(t => t.Usuario).ThenInclude(u => u.Perfil)
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash && t.RevogadoEm == null, ct);

    public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}

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

    /// <summary>
    /// Sem filtro de estado, de proposito (ver o contrato da interface). Rastreado como o
    /// <see cref="ObterAtivoPorHashAsync"/>: o caminho feliz da rotacao muta o registro e conta com
    /// o change tracking para revoga-lo no mesmo <c>SaveChanges</c> que insere o novo.
    /// </summary>
    public Task<RefreshToken?> ObterPorHashAsync(string tokenHash, CancellationToken ct) =>
        _db.RefreshTokens
            .Include(t => t.Usuario).ThenInclude(u => u.Perfil)
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    /// <summary>
    /// <c>ExecuteUpdateAsync</c> emite um unico UPDATE no servidor e ja commita — nao carrega as
    /// linhas para a memoria nem depende de um <c>SaveChanges</c> posterior. Em troca, ele nao
    /// passa pelo change tracker: entidades ja rastreadas nesta requisicao continuam com o
    /// <c>RevogadoEm</c> antigo em memoria. O unico chamador (o caminho de reuso do
    /// <c>RenovarTokenUseCase</c>) descarta a entidade que tinha lido e retorna 401 logo em
    /// seguida, entao nao ha estado desatualizado sendo reaproveitado.
    /// </summary>
    public Task<int> RevogarTodosAtivosDoUsuarioAsync(
        int usuarioId, DateTime revogadoEm, CancellationToken ct) =>
        _db.RefreshTokens
            .Where(t => t.UsuarioId == usuarioId && t.RevogadoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevogadoEm, revogadoEm), ct);

    /// <summary>
    /// Traduz <see cref="DbUpdateConcurrencyException"/> (RowVersion obsoleto — outra requisicao
    /// alterou a linha entre a leitura e este save) para <see cref="ConflitoDeConcorrenciaException"/>,
    /// que a Application pode capturar sem referenciar o EF Core.
    /// </summary>
    public async Task SalvarAlteracoesAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflitoDeConcorrenciaException(ex);
        }
    }
}

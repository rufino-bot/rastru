using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Auth;

public class RevogarTokenUseCase : IRevogarTokenUseCase
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ITokenHasher _tokenHasher;

    public RevogarTokenUseCase(IRefreshTokenRepository refreshTokens, ITokenHasher tokenHasher)
    {
        _refreshTokens = refreshTokens;
        _tokenHasher = tokenHasher;
    }

    /// <summary>
    /// Logout: revoga o refresh token informado. E idempotente — token ausente ou ja
    /// revogado nao lanca nem sinaliza erro (o repositorio so devolve tokens nao revogados).
    /// </summary>
    public async Task ExecutarAsync(string refreshTokenPlano, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenPlano)) return;

        var atual = await _refreshTokens.ObterAtivoPorHashAsync(_tokenHasher.Hash(refreshTokenPlano), ct);
        if (atual is null) return;

        // Nao checa ExpiraEm: revogar um token ja expirado e inofensivo e evita
        // deixar linha "ativa" no banco por conta de uma corrida com o relogio.
        atual.RevogadoEm = DateTime.UtcNow;
        await _refreshTokens.SalvarAlteracoesAsync(ct);
    }
}

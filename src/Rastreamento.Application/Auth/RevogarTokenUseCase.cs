using Rastreamento.Application.Common;
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
    /// Logout: revoga o refresh token informado. E idempotente — token ausente, desconhecido ou
    /// ja revogado nao lanca nem sinaliza erro (o repositorio so devolve tokens nao revogados).
    /// Por isso o retorno e sempre <c>Ok</c>: "nao havia nada para revogar" e sucesso, e
    /// distinguir os casos vazaria a existencia do token.
    /// </summary>
    public async Task<Result> ExecutarAsync(string refreshTokenPlano, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenPlano)) return Result.Ok();

        var atual = await _refreshTokens.ObterAtivoPorHashAsync(_tokenHasher.Hash(refreshTokenPlano), ct);
        if (atual is null) return Result.Ok();

        // Nao checa ExpiraEm: revogar um token ja expirado e inofensivo e evita
        // deixar linha "ativa" no banco por conta de uma corrida com o relogio.
        atual.RevogadoEm = DateTime.UtcNow;
        await _refreshTokens.SalvarAlteracoesAsync(ct);
        return Result.Ok();
    }
}

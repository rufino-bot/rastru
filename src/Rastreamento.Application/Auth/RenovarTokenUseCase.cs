using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Auth;

public class RenovarTokenUseCase : IRenovarTokenUseCase
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ITokenHasher _tokenHasher;
    private readonly IEmissorDeSessao _emissor;

    public RenovarTokenUseCase(
        IRefreshTokenRepository refreshTokens,
        ITokenHasher tokenHasher,
        IEmissorDeSessao emissor)
    {
        _refreshTokens = refreshTokens;
        _tokenHasher = tokenHasher;
        _emissor = emissor;
    }

    public async Task<Result<LoginResult>> ExecutarAsync(string refreshTokenPlano, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenPlano))
            return Result<LoginResult>.Falha("Refresh token inválido ou expirado.", TipoDeErro.NaoAutorizado);

        var hash = _tokenHasher.Hash(refreshTokenPlano);
        var atual = await _refreshTokens.ObterAtivoPorHashAsync(hash, ct);

        // `!atual.Usuario.Ativo`: sem isso, desativar um usuario nao o expulsa — ele
        // continuaria rotacionando o refresh ate a expiracao natural (ate 7 dias).
        // O login checa Ativo; o refresh TEM que checar tambem.
        // `atual.RevogadoEm is not null` e redundante com o contrato do repositorio
        // (que ja filtra RevogadoEm IS NULL), mas fica de proposito: numa fronteira de
        // seguranca, uma implementacao futura descuidada do repositorio nao pode virar
        // reuso de token revogado.
        if (atual is null
            || atual.RevogadoEm is not null
            || atual.ExpiraEm <= DateTime.UtcNow
            || !atual.Usuario.Ativo)
            return Result<LoginResult>.Falha("Refresh token inválido ou expirado.", TipoDeErro.NaoAutorizado);

        // Revogação do antigo + emissão do novo num único save (ver EmissorDeSessao).
        var novaSessao = await _emissor.RotacionarAsync(atual, ct);
        return Result<LoginResult>.Ok(novaSessao);
    }
}

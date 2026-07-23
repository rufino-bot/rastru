using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IRefreshTokenRepository
{
    Task AdicionarAsync(RefreshToken token, CancellationToken ct);

    /// <summary>
    /// Retorna o token nao revogado (<c>RevogadoEm IS NULL</c>) correspondente ao hash informado,
    /// com <c>Usuario</c> e <c>Perfil</c> carregados. Nao filtra por expiracao: quem verifica
    /// <c>ExpiraEm</c> e o caso de uso.
    /// </summary>
    Task<RefreshToken?> ObterAtivoPorHashAsync(string tokenHash, CancellationToken ct);

    Task SalvarAlteracoesAsync(CancellationToken ct);
}

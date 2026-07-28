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

    /// <summary>
    /// Retorna o token correspondente ao hash em QUALQUER estado — inclusive ja revogado —, com
    /// <c>Usuario</c> e <c>Perfil</c> carregados e rastreado. E o que torna a reapresentacao de um
    /// token ja rotacionado visivel: <see cref="ObterAtivoPorHashAsync"/> nunca enxerga esse caso,
    /// entao com ele o sinal de reuso e indistinguivel de "token nunca existiu".
    /// </summary>
    Task<RefreshToken?> ObterPorHashAsync(string tokenHash, CancellationToken ct);

    /// <summary>
    /// Marca <c>RevogadoEm = revogadoEm</c> em todos os refresh tokens ainda ativos do usuario e
    /// persiste, num unico comando. Devolve quantas linhas foram revogadas. Usado na deteccao de
    /// reuso: um refresh vazado derruba a familia inteira de sessoes daquele usuario.
    /// </summary>
    Task<int> RevogarTodosAtivosDoUsuarioAsync(int usuarioId, DateTime revogadoEm, CancellationToken ct);

    Task SalvarAlteracoesAsync(CancellationToken ct);
}

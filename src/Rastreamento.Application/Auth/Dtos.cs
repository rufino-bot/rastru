using Rastreamento.Application.Common;

namespace Rastreamento.Application.Auth;

public record LoginRequest(string NomeUsuario, string Senha);

public record UsuarioDto(int Id, string NomeUsuario, string NomeCompleto, string Perfil);

public record LoginResult(
    string AccessToken,
    DateTime AccessTokenExpiraEm,
    string RefreshTokenPlano,
    DateTime RefreshTokenExpiraEm,
    UsuarioDto Usuario);

public interface IAutenticarUsuarioUseCase
{
    Task<Result<LoginResult>> ExecutarAsync(LoginRequest req, CancellationToken ct);
}

// Implementados na T7 (renovacao/revogacao de refresh token).
public interface IRenovarTokenUseCase
{
    Task<Result<LoginResult>> ExecutarAsync(string refreshTokenPlano, CancellationToken ct);
}

public interface IRevogarTokenUseCase
{
    /// <summary>
    /// Sempre bem-sucedido: logout e idempotente por design (ver <c>RevogarTokenUseCase</c>).
    /// Devolve <see cref="Result"/> em vez de <c>Task</c> pelado so para nao ser a excecao da
    /// convencao dos casos de uso — o endpoint de logout ignora o resultado de proposito.
    /// </summary>
    Task<Result> ExecutarAsync(string refreshTokenPlano, CancellationToken ct);
}

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
    Task ExecutarAsync(string refreshTokenPlano, CancellationToken ct);
}

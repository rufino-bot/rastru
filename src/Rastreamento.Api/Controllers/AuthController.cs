using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Auth;

namespace Rastreamento.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private const string NomeDoCookieDeRefresh = "refreshToken";

    private readonly IAutenticarUsuarioUseCase _autenticar;
    private readonly IRenovarTokenUseCase _renovar;
    private readonly IRevogarTokenUseCase _revogar;

    public AuthController(
        IAutenticarUsuarioUseCase autenticar,
        IRenovarTokenUseCase renovar,
        IRevogarTokenUseCase revogar)
    {
        _autenticar = autenticar;
        _renovar = renovar;
        _revogar = revogar;
    }

    public record LoginBody(string NomeUsuario, string Senha);

    /// <remarks>
    /// <c>AccessTokenExpiraEm</c> sai daqui em UTC; quem converte para GMT-3 e o
    /// <see cref="Serialization.HorarioDeBrasiliaJsonConverter"/>, na serializacao.
    /// </remarks>
    public record LoginResponse(
        string AccessToken,
        DateTime AccessTokenExpiraEm,
        UsuarioDto Usuario);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginBody body, CancellationToken ct)
    {
        var resultado = await _autenticar.ExecutarAsync(
            new LoginRequest(body.NomeUsuario, body.Senha), ct);

        if (!resultado.Sucesso) return Unauthorized(new { erro = resultado.Erro });

        return Ok(EntregarSessao(resultado.Valor!));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var refreshPlano = Request.Cookies[NomeDoCookieDeRefresh] ?? string.Empty;
        var resultado = await _renovar.ExecutarAsync(refreshPlano, ct);

        if (!resultado.Sucesso) return Unauthorized(new { erro = resultado.Erro });

        return Ok(EntregarSessao(resultado.Valor!));
    }

    /// <summary>
    /// Logout e idempotente: cookie ausente, token desconhecido, expirado ou ja revogado
    /// respondem 204 do mesmo jeito — responder diferente vazaria a existencia do token.
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var refreshPlano = Request.Cookies[NomeDoCookieDeRefresh] ?? string.Empty;
        await _revogar.ExecutarAsync(refreshPlano, ct);

        Response.Cookies.Delete(NomeDoCookieDeRefresh, new CookieOptions { Path = "/auth" });
        return NoContent();
    }

    /// <summary>
    /// Entrega a sessao recem-emitida: o refresh token so sai por cookie httpOnly (nunca no
    /// corpo) e o access token so sai no corpo (nunca em cookie).
    /// </summary>
    private LoginResponse EntregarSessao(LoginResult sessao)
    {
        Response.Cookies.Append(NomeDoCookieDeRefresh, sessao.RefreshTokenPlano, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/auth",
            // Em UTC mesmo: o header Set-Cookie sempre serializa a expiracao em GMT, entao
            // converter aqui seria no-op (e sugeriria, falsamente, que o fuso importa).
            Expires = sessao.RefreshTokenExpiraEm,
        });

        return new LoginResponse(sessao.AccessToken, sessao.AccessTokenExpiraEm, sessao.Usuario);
    }
}

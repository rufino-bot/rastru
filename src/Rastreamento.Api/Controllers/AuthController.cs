using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Rastreamento.Api.Configuration;
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

    /// <remarks>
    /// Falha sempre vira 401, sem consultar <c>TipoDoErro</c>: aqui toda falha e de autenticacao
    /// e responder qualquer outra coisa distinguiria os casos (usuario inexistente, inativo,
    /// senha errada) para quem chama.
    /// </remarks>
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitOptions.NomeDaPoliticaDeLogin)]
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

        // Resultado ignorado de proposito: a resposta e 204 em qualquer cenario.
        _ = await _revogar.ExecutarAsync(refreshPlano, ct);

        // Os mesmos atributos do cookie original: o navegador so casa a instrucao de remocao com
        // o cookie existente se Path, Secure, SameSite e HttpOnly baterem.
        Response.Cookies.Delete(NomeDoCookieDeRefresh, AtributosDoCookieDeRefresh());
        return NoContent();
    }

    /// <summary>
    /// Entrega a sessao recem-emitida: o refresh token so sai por cookie httpOnly (nunca no
    /// corpo) e o access token so sai no corpo (nunca em cookie).
    /// </summary>
    private LoginResponse EntregarSessao(LoginResult sessao)
    {
        var cookie = AtributosDoCookieDeRefresh();
        // Em UTC mesmo: o header Set-Cookie sempre serializa a expiracao em GMT, entao converter
        // aqui seria no-op (e sugeriria, falsamente, que o fuso importa).
        cookie.Expires = sessao.RefreshTokenExpiraEm;
        Response.Cookies.Append(NomeDoCookieDeRefresh, sessao.RefreshTokenPlano, cookie);

        return new LoginResponse(sessao.AccessToken, sessao.AccessTokenExpiraEm, sessao.Usuario);
    }

    /// <summary>
    /// Atributos do cookie de refresh, num lugar so: <c>Path=/auth</c> mantem o cookie fora das
    /// demais rotas, e httpOnly + Secure + SameSite=Strict sao o que impede JavaScript e
    /// requisicao cross-site de alcanca-lo. Emissao e remocao usam exatamente os mesmos valores.
    /// </summary>
    private static CookieOptions AtributosDoCookieDeRefresh() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/auth",
    };
}

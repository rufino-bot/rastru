using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Auth;

namespace Rastreamento.Api.Controllers;

[ApiController]
[Route("me")]
[Authorize]
public class MeController : ControllerBase
{
    /// <summary>
    /// Devolve o usuario da sessao a partir das claims do access token — sem ida ao banco,
    /// ja que o proprio token, assinado por nos, carrega tudo o que a tela precisa.
    /// </summary>
    [HttpGet]
    public IActionResult Get()
    {
        var id = User.FindFirst("sub")?.Value;
        var nomeUsuario = User.FindFirst("unique_name")?.Value;
        var nomeCompleto = User.FindFirst("nome_completo")?.Value;
        var perfil = User.FindFirst("role")?.Value;

        // Token assinado por nos mas sem as claims que emitimos e falha de autenticacao (401),
        // nao erro do servidor: com `!` a claim ausente virava NullReferenceException e 500.
        if (!int.TryParse(id, out var usuarioId)
            || string.IsNullOrEmpty(nomeUsuario)
            || string.IsNullOrEmpty(nomeCompleto)
            || string.IsNullOrEmpty(perfil))
            return Unauthorized();

        return Ok(new UsuarioDto(usuarioId, nomeUsuario, nomeCompleto, perfil));
    }
}

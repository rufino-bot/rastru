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
        var id = int.Parse(User.FindFirst("sub")!.Value);
        var nomeUsuario = User.FindFirst("unique_name")!.Value;
        var nomeCompleto = User.FindFirst("nome_completo")!.Value;
        var perfil = User.FindFirst("role")!.Value;

        return Ok(new UsuarioDto(id, nomeUsuario, nomeCompleto, perfil));
    }
}

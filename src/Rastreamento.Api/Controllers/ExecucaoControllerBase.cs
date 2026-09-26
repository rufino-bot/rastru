using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Common;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// O que os controllers da Fase 3 fazem igual: traduzir `Result` em status e corpo
/// `{ erro, mensagem }` (spec da Fase 3, secao 8.2) e ler a sessao. Abstrata, sem rota nem
/// `[Authorize]`: perfil e decisao de cada controller concreto, e `permissoesEspelhamOBackend.test.ts`
/// isenta este arquivo conferindo que ele nunca declara `Roles`.
/// </summary>
public abstract class ExecucaoControllerBase : ControllerBase
{
  protected IActionResult Traduzir<T>(Result<T> r, bool criado = false)
  {
    if (r.Sucesso) return criado ? StatusCode(StatusCodes.Status201Created, r.Valor) : Ok(r.Valor);
    return Recusar(r.TipoDoErro, r.Erro, r.Detalhe);
  }

  /// <summary>
  /// 404 com `NotFound()`, sem `erro`, como `EstruturaController.Recusar`. O 403 daqui e o do CASO DE USO (`Proibido`) e
  /// leva corpo; o 403 do `[Authorize(Roles)]` nem chega a action.
  /// </summary>
  protected IActionResult Recusar(TipoDeErro? tipo, string? erro, string? detalhe)
  {
    if (tipo == TipoDeErro.NaoEncontrado) return NotFound();

    object corpo = detalhe is null ? new { erro } : new { erro, mensagem = detalhe };
    return tipo switch
    {
      TipoDeErro.Conflito => Conflict(corpo),
      TipoDeErro.Proibido => StatusCode(StatusCodes.Status403Forbidden, corpo),
      _ => BadRequest(corpo),
    };
  }

  /// <summary>Mesmo criterio de `CadastroControllerBase.UsuarioDaSessao`: sem `sub`, 401.</summary>
  protected int? UsuarioDaSessao() => int.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;

  /// <summary>Quem estorna registro alheio (spec secao 4.5). `RoleClaimType = "role"` no `Program.cs`.</summary>
  protected bool EhPcpOuAdministrador() => User.IsInRole("PCP") || User.IsInRole("Administrador");
}

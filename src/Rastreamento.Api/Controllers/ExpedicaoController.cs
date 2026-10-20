using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Expedicao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// A Qualidade no local de expedicao (D1 da spec da Fase 5A): aferir, contar e mandar para conserto. Um
/// controller por conjunto de perfis (decisao P7 do plano de backend da Fase 5A): todo `[Authorize(Roles)]`
/// deste arquivo espelha UMA entrada de `permissoes.ts`.
/// </summary>
[ApiController]
[Authorize]
public class ExpedicaoController : ExecucaoControllerBase
{
  /// <summary>Spec da Fase 5A, secao 5.2: o que a expedicao escreve e da Qualidade.</summary>
  private const string PerfisDeEscrita = "Qualidade,Administrador";

  private readonly AfericaoUseCase _afericao;

  public ExpedicaoController(AfericaoUseCase afericao) => _afericao = afericao;

  [HttpPost("estrutura/{id:int}/afericoes")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Aferir(int id, [FromBody] NovaAfericaoDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _afericao.Aferir(id, dto, usuarioId, ct), criado: true);
  }

  [HttpPost("estrutura/{id:int}/contagens")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Contar(int id, [FromBody] NovaContagemDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _afericao.Contar(id, dto, usuarioId, ct), criado: true);
  }

  [HttpPost("estrutura/{id:int}/consertos")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> MandarParaConserto(int id, [FromBody] NovoConsertoDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _afericao.MandarParaConserto(id, dto, usuarioId, ct), criado: true);
  }
}

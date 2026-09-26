using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Execucao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// O Roteiro de um no (spec secao 4.6). Controller proprio, e nao carona no `EstruturaController`, embora
/// os perfis hoje coincidam: e o `Recurso` `roteiro` do front, e a primeira vez que os perfis divergirem
/// a carona seria descoberta como bug.
/// </summary>
[ApiController]
[Authorize]
public class RoteiroDoNoController : ExecucaoControllerBase
{
  /// <summary>Spec secao 4.8: editar o Roteiro do no e do PCP.</summary>
  private const string PerfisDeEscrita = "PCP,Administrador";

  private readonly RoteiroDoNoUseCase _roteiro;

  public RoteiroDoNoController(RoteiroDoNoUseCase roteiro) => _roteiro = roteiro;

  [HttpGet("estrutura/{id:int}/roteiro")]
  public async Task<IActionResult> Obter(int id, CancellationToken ct) => Traduzir(await _roteiro.Obter(id, ct));

  [HttpPut("estrutura/{id:int}/roteiro")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Substituir(int id, [FromBody] RoteiroNovoDto dto, CancellationToken ct) =>
      Traduzir(await _roteiro.Substituir(id, dto, ct));
}

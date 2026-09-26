using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Execucao;

namespace Rastreamento.Api.Controllers;

/// <summary>O Movimentador: as tarefas que ele le e a entrega em lista que ele registra.</summary>
[ApiController]
[Authorize]
public class EntregaController : ExecucaoControllerBase
{
  /// <summary>Spec secao 4.8: entregar (inclusive redirecionar) e do Movimentador.</summary>
  private const string PerfisDeEscrita = "Movimentador,Administrador";

  private readonly EntregaUseCase _entrega;
  private readonly ConsultaDeExecucaoUseCase _consulta;

  public EntregaController(EntregaUseCase entrega, ConsultaDeExecucaoUseCase consulta)
  {
    _entrega = entrega;
    _consulta = consulta;
  }

  [HttpPost("entregas")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Entregar([FromBody] EntregaDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _entrega.Entregar(dto, usuarioId, ct), criado: true);
  }

  [HttpGet("tarefas")]
  public async Task<IActionResult> Tarefas(CancellationToken ct) => Traduzir(await _consulta.Tarefas(ct));

  [HttpGet("tarefas/contagem")]
  public async Task<IActionResult> Contagem(CancellationToken ct) => Traduzir(await _consulta.ContagemDeTarefas(ct));
}

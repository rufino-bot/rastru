using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Execucao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// O que o Operador registra no Setor — iniciar, terminar, montar — e a fila que ele le. Sem `[Route]`
/// de classe: as rotas de no sao `estrutura/{id}/...` (o prefixo da Fase 2) e a fila e `setores/{id}/fila`.
/// </summary>
[ApiController]
[Authorize]
public class ApontamentoController : ExecucaoControllerBase
{
  /// <summary>Spec secao 4.8: iniciar, terminar e montar sao do Operador.</summary>
  private const string PerfisDeEscrita = "Operador,Administrador";

  private readonly ApontamentoUseCase _apontamento;
  private readonly ConsultaDeExecucaoUseCase _consulta;

  public ApontamentoController(ApontamentoUseCase apontamento, ConsultaDeExecucaoUseCase consulta)
  {
    _apontamento = apontamento;
    _consulta = consulta;
  }

  [HttpPost("estrutura/{id:int}/inicios")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Iniciar(int id, [FromBody] InicioDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _apontamento.Iniciar(id, dto, usuarioId, ct), criado: true);
  }

  [HttpPost("estrutura/{id:int}/terminos")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Terminar(int id, [FromBody] TerminoDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _apontamento.Terminar(id, dto, usuarioId, ct), criado: true);
  }

  [HttpPost("estrutura/{id:int}/montagens")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Montar(int id, [FromBody] MontagemNovaDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _apontamento.Montar(id, dto, usuarioId, ct), criado: true);
  }

  [HttpGet("setores/{id:int}/fila")]
  public async Task<IActionResult> Fila(int id, CancellationToken ct) => Traduzir(await _consulta.Fila(id, ct));
}

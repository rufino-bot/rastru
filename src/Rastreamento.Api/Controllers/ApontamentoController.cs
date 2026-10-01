using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Execucao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// O que o Operador registra no Setor — iniciar, terminar, de um no ou em lote — e a fila que ele le.
/// Sem `[Route]` de classe: as rotas de no sao `estrutura/{id}/...` (o prefixo da Fase 2), as de lote sao
/// `inicios` e `terminos` (no molde de `entregas`) e a fila e `setores/{id}/fila`.
/// </summary>
[ApiController]
[Authorize]
public class ApontamentoController : ExecucaoControllerBase
{
  /// <summary>Spec secao 4.8: iniciar e terminar sao do Operador.</summary>
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

  /// <summary>Spec dos filtros e do lote, secao 6.1: os mesmos perfis da rota de um no.</summary>
  [HttpPost("inicios")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> IniciarEmLote([FromBody] LoteDeInicioDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _apontamento.IniciarEmLote(dto, usuarioId, ct), criado: true);
  }

  [HttpPost("terminos")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> TerminarEmLote([FromBody] LoteDeTerminoDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _apontamento.TerminarEmLote(dto, usuarioId, ct), criado: true);
  }

  [HttpGet("setores/{id:int}/fila")]
  public async Task<IActionResult> Fila(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _consulta.Fila(id, new QuemLe(usuarioId, EhPcpOuAdministrador()), ct));
  }
}

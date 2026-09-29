using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Execucao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// Pausar e retomar um Pedido (spec da Fase 3D, secao 2.5). Controller proprio, e nao acao do
/// `PedidosController`: a guarda do front compara todo `[Authorize(Roles)]` de um arquivo com UMA
/// entrada de `permissoes.ts`, e os perfis daqui (com a Gestao) nao sao os do cadastro de Pedido.
/// </summary>
[ApiController]
[Authorize]
public class PausaDePedidoController : ExecucaoControllerBase
{
  /// <summary>Planejamento decide prioridade: PCP e Gestao — a primeira escrita da Gestao no sistema.</summary>
  private const string PerfisDeEscrita = "PCP,Gestao,Administrador";

  private readonly PausaDePedidoUseCase _pausa;

  public PausaDePedidoController(PausaDePedidoUseCase pausa) => _pausa = pausa;

  [HttpPost("pedidos/{id:int}/pausas")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Pausar(int id, [FromBody] NovaPausaDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _pausa.Pausar(id, dto, usuarioId, ct), criado: true);
  }

  [HttpPost("pedidos/{id:int}/retomada")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Retomar(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _pausa.Retomar(id, usuarioId, ct));
  }
}

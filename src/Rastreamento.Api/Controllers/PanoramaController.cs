using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Panorama;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// O Panorama (spec do Panorama, secao 5.1): onde estao as unidades de cada Peca de Pedido nao encerrado. Leitura de
/// qualquer perfil autenticado (C16), sem `Roles`.
/// </summary>
[ApiController]
[Authorize]
public class PanoramaController : ExecucaoControllerBase
{
  private readonly ConsultaDoPanoramaUseCase _consulta;

  public PanoramaController(ConsultaDoPanoramaUseCase consulta) => _consulta = consulta;

  [HttpGet("panorama")]
  public async Task<IActionResult> Lista(CancellationToken ct) => Traduzir(await _consulta.Lista(ct));
}

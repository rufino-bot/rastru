using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Panorama;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// O Panorama (spec do Panorama, secao 5.1): onde estao as unidades de cada Peca de Pedido nao encerrado. Leitura de
/// qualquer perfil autenticado (C16), sem `Roles`; marcar e desmarcar o "Embalado" (secao 5.2) e da Qualidade, do PCP e
/// do Administrador (C17).
/// </summary>
[ApiController]
[Authorize]
public class PanoramaController : ExecucaoControllerBase
{
  /// <summary>Spec do Panorama, C17: espelha a entrada `embalagem` de `permissoes.ts`.</summary>
  private const string PerfisDeEscrita = "Qualidade,PCP,Administrador";

  private readonly ConsultaDoPanoramaUseCase _consulta;
  private readonly EmbalagemUseCase _embalagem;

  public PanoramaController(ConsultaDoPanoramaUseCase consulta, EmbalagemUseCase embalagem)
  {
    _consulta = consulta;
    _embalagem = embalagem;
  }

  [HttpGet("panorama")]
  public async Task<IActionResult> Lista(CancellationToken ct) => Traduzir(await _consulta.Lista(ct));

  /// <summary>`{ "embalado": true }` marca e `false` desmarca; 204 em sucesso, as recusas no contrato da execucao.</summary>
  [HttpPost("estrutura/{id:int}/embalagem")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Registrar(int id, [FromBody] NovaEmbalagemDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    var r = await _embalagem.Registrar(id, dto, usuarioId, ct);
    return r.Sucesso ? NoContent() : Recusar(r.TipoDoErro, r.Erro, r.Detalhe);
  }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Baixa;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// A baixa (secao 5.1 da spec da Fase 5B-1): tirar quantidade de producao e leva-la a `Perdido`, com um motivo. Um
/// controller so para todas as escritas da baixa, as previas inclusive, e as leituras dela (decisao P1 do plano de
/// backend da Fase 5B-1): todo `[Authorize(Roles)]` deste arquivo espelha UMA entrada de `permissoes.ts`, `baixa`.
/// </summary>
[ApiController]
[Authorize]
public class BaixaController : ExecucaoControllerBase
{
  /// <summary>D5 da spec da Fase 5B-1: so a Qualidade e o PCP dao baixa, mais o Administrador.</summary>
  private const string PerfisDeEscrita = "Qualidade,PCP,Administrador";

  private readonly BaixaUseCase _baixa;

  public BaixaController(BaixaUseCase baixa) => _baixa = baixa;

  /// <summary>Da baixa de parte do no ou de tudo dele (D3, D4, D11), na mesma `Perda`, com a Peca do topo se pedida (D13, D14).</summary>
  [HttpPost("estrutura/{id:int}/baixas")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> DarBaixa(int id, [FromBody] NovaBaixaDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _baixa.DarBaixa(id, dto, usuarioId, ct), criado: true);
  }

  /// <summary>O que a baixa gravaria, sem gravar (C14): calcula no servidor, com as mesmas recusas.</summary>
  [HttpPost("estrutura/{id:int}/baixas/previa")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> PreverBaixa(int id, [FromBody] NovaBaixaDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is null) return Unauthorized();
    return Traduzir(await _baixa.PreverBaixa(id, dto, ct));
  }
}

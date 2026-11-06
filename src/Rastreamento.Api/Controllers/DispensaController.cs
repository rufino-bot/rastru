using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Retrabalho;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// O "nao repor" e desfaze-lo (D3, C6 e C15 da spec da Fase 5B-2, secao 4.6): so o PCP e o Administrador. Controller
/// proprio porque o conjunto de perfis e outro (decisao P1 do plano de backend da Fase 5B-2): todo `[Authorize(Roles)]`
/// deste arquivo espelha UMA entrada de `permissoes.ts`.
/// </summary>
[ApiController]
[Authorize]
public class DispensaController : ExecucaoControllerBase
{
  /// <summary>C6 da spec da Fase 5B-2: "nao repor" e desfaze-lo sao do PCP e do Administrador.</summary>
  private const string PerfisDeEscrita = "PCP,Administrador";

  private readonly DispensaUseCase _dispensa;

  public DispensaController(DispensaUseCase dispensa) => _dispensa = dispensa;

  /// <summary>Dispensa a reposicao da baixa de perda `{id}` com a justificativa: 201 com a dispensa.</summary>
  [HttpPost("baixas/{id:int}/dispensa")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> DispensarBaixa(int id, [FromBody] NovaDispensaDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _dispensa.DispensarBaixa(id, dto, usuarioId, ct), criado: true);
  }

  /// <summary>Dispensa a reposicao da reclamacao `{id}` com a justificativa: 201 com a dispensa.</summary>
  [HttpPost("reclamacoes/{id:int}/dispensa")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> DispensarReclamacao(int id, [FromBody] NovaDispensaDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _dispensa.DispensarReclamacao(id, dto, usuarioId, ct), criado: true);
  }

  /// <summary>Desfaz o "nao repor": 204, e a dispensa fica marcada como desfeita.</summary>
  [HttpPost("dispensas/{id:int}/estorno")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Estornar(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    var resultado = await _dispensa.Estornar(id, usuarioId, ct);
    return resultado.Sucesso ? NoContent() : Recusar(resultado.TipoDoErro, resultado.Erro, resultado.Detalhe);
  }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Expedicao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// A Gestao responde ao pedido de liberacao da Qualidade (D7 e C5 da spec da Fase 5A). Controller proprio porque
/// o conjunto de perfis e outro (decisao P7 do plano de backend da Fase 5A): todo `[Authorize(Roles)]` deste
/// arquivo espelha UMA entrada de `permissoes.ts`.
/// </summary>
[ApiController]
[Authorize]
public class LiberacaoController : ExecucaoControllerBase
{
  /// <summary>Spec da Fase 5A, secao 5.2: responder liberacao e da Gestao.</summary>
  private const string PerfisDeEscrita = "Gestao,Administrador";

  private readonly LiberacaoUseCase _liberacao;

  public LiberacaoController(LiberacaoUseCase liberacao) => _liberacao = liberacao;

  [HttpPost("liberacoes/{id:int}/resposta")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Responder(int id, [FromBody] RespostaDeLiberacaoDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _liberacao.Responder(id, dto, usuarioId, ct), criado: true);
  }
}

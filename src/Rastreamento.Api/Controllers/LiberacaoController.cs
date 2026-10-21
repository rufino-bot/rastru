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
  private readonly ConsultaDaExpedicaoUseCase _consulta;

  public LiberacaoController(LiberacaoUseCase liberacao, ConsultaDaExpedicaoUseCase consulta)
  {
    _liberacao = liberacao;
    _consulta = consulta;
  }

  [HttpPost("liberacoes/{id:int}/resposta")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Responder(int id, [FromBody] RespostaDeLiberacaoDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _liberacao.Responder(id, dto, usuarioId, ct), criado: true);
  }

  /// <summary>Os pedidos que a Gestao ainda nao respondeu, do mais antigo ao mais novo. Leitura: de todo perfil autenticado.</summary>
  [HttpGet("liberacoes/pendentes")]
  public async Task<IActionResult> Pendentes(CancellationToken ct) =>
      Traduzir(await _consulta.LiberacoesPendentes(ct));
}

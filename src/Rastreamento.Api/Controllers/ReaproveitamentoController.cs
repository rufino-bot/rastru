using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Retrabalho;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// "Usar a sobra" no Retrabalho e a previa dele (secao 4.4 da spec da Fase 5B-2). Controller proprio porque o conjunto
/// de perfis e outro (decisao P1 do plano de backend da Fase 5B-2): todo `[Authorize(Roles)]` deste arquivo espelha UMA
/// entrada de `permissoes.ts`. O estorno e o da baixa `Reaproveitado`, pela rota da baixa (`BaixaController`).
/// </summary>
[ApiController]
[Authorize]
public class ReaproveitamentoController : ExecucaoControllerBase
{
  /// <summary>C6 da spec da Fase 5B-2: quem cadastra o Retrabalho usa a sobra, o PCP e o Administrador.</summary>
  private const string PerfisDeEscrita = "PCP,Administrador";

  private readonly ReaproveitamentoUseCase _reaproveitamento;

  public ReaproveitamentoController(ReaproveitamentoUseCase reaproveitamento) => _reaproveitamento = reaproveitamento;

  /// <summary>A sobra viva do original que o Retrabalho `{id}` pode receber como pronto, com a sugestao. Sem gravar.</summary>
  [HttpGet("pedidos/{id:int}/sobra-reaproveitavel")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Prever(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is null) return Unauthorized();
    return Traduzir(await _reaproveitamento.Prever(id, ct));
  }

  /// <summary>Usa a sobra no Retrabalho `{id}`: 201 com a baixa `Reaproveitado`, que e do Pedido original.</summary>
  [HttpPost("pedidos/{id:int}/sobra-reaproveitada")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Reaproveitar(int id, [FromBody] NovoReaproveitamentoDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _reaproveitamento.Reaproveitar(id, dto, usuarioId, ct), criado: true);
  }
}

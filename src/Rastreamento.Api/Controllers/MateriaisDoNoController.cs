using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Separacao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// Os materiais planejados de um no (spec da Fase 4, D8). Controller proprio, um por conjunto de perfis
/// (decisao P6 do plano de backend da Fase 4): a guarda de espelhamento do front compara todo
/// `[Authorize(Roles)]` de um arquivo com UMA entrada de `permissoes.ts`, e a separacao e o estorno dela
/// tem perfis diferentes destes.
/// </summary>
[ApiController]
[Authorize]
public class MateriaisDoNoController : ExecucaoControllerBase
{
  /// <summary>Editar o planejado do no e do PCP; a leitura e de qualquer autenticado.</summary>
  private const string PerfisDeEscrita = "PCP,Administrador";

  private readonly MateriaisDoNoUseCase _materiais;

  public MateriaisDoNoController(MateriaisDoNoUseCase materiais) => _materiais = materiais;

  [HttpGet("estrutura/{id:int}/materiais")]
  public async Task<IActionResult> Obter(int id, CancellationToken ct) => Traduzir(await _materiais.Obter(id, ct));

  [HttpPut("estrutura/{id:int}/materiais")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Substituir(int id, [FromBody] MateriaisNovosDoNoDto dto, CancellationToken ct) =>
      Traduzir(await _materiais.Substituir(id, dto, ct));
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Retrabalho;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// A reclamacao do cliente (D4 e C13 da spec da Fase 5B-2): registrar, listar e estornar. Controller proprio porque o
/// conjunto de perfis e outro (decisao P1 do plano de backend da Fase 5B-2): todo `[Authorize(Roles)]` deste arquivo
/// espelha UMA entrada de `permissoes.ts`.
/// </summary>
[ApiController]
[Authorize]
public class ReclamacaoController : ExecucaoControllerBase
{
  /// <summary>C6 da spec da Fase 5B-2: a Qualidade, o PCP e o Administrador registram e estornam.</summary>
  private const string PerfisDeEscrita = "Qualidade,PCP,Administrador";

  private readonly ReclamacaoUseCase _reclamacao;

  public ReclamacaoController(ReclamacaoUseCase reclamacao) => _reclamacao = reclamacao;

  [HttpPost("pedidos/{id:int}/reclamacoes")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Registrar(int id, [FromBody] NovaReclamacaoDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _reclamacao.Registrar(id, dto, usuarioId, ct), criado: true);
  }

  /// <summary>Estorna a reclamacao: o autor, ou PCP e Administrador (403 `Proibido` do caso de uso).</summary>
  [HttpPost("reclamacoes/{id:int}/estorno")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Estornar(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _reclamacao.Estornar(id, usuarioId, EhPcpOuAdministrador(), ct));
  }

  /// <summary>As reclamacoes do Pedido, mais recente primeiro. Qualquer autenticado.</summary>
  [HttpGet("pedidos/{id:int}/reclamacoes")]
  public async Task<IActionResult> Listar(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _reclamacao.Listar(id, new QuemLe(usuarioId, EhPcpOuAdministrador()), ct));
  }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Separacao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// Estornar uma linha do livro de separacao. O `[Authorize]` deixa passar quem pode ser autor (o
/// Separador) e o PCP; "o autor ou PCP/Administrador" e decisao do caso de uso, que devolve 403 `Proibido`
/// com corpo, como em `EstornoController`. Controller proprio pela decisao P6 do plano de backend da Fase 4.
/// </summary>
[ApiController]
[Authorize]
public class EstornoDeSeparacaoController : ExecucaoControllerBase
{
  private const string PerfisDeEscrita = "Separador,PCP,Administrador";

  private readonly SeparacaoUseCase _separacao;

  public EstornoDeSeparacaoController(SeparacaoUseCase separacao) => _separacao = separacao;

  [HttpPost("separacoes/{id:int}/estorno")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Estornar(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _separacao.Estornar(id, usuarioId, EhPcpOuAdministrador(), ct), criado: true);
  }
}

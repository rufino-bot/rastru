using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Execucao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// Estornar e ler o livro do no. O `[Authorize]` deixa passar quem pode ser autor de um registro, mais o
/// PCP; "o autor ou PCP/Administrador" e decisao do caso de uso, que devolve 403 `Proibido` com corpo
/// (spec secoes 4.5 e 5.1). Os dois estornos declaram os mesmos perfis porque vivem aqui (desvio D2 do
/// plano 2): no de montagem, o Movimentador nunca e autor, e cai no 403 do caso de uso.
/// </summary>
[ApiController]
[Authorize]
public class EstornoController : ExecucaoControllerBase
{
  private const string PerfisDeEscrita = "Operador,Movimentador,PCP,Administrador";

  private readonly EstornoUseCase _estorno;
  private readonly ConsultaDeExecucaoUseCase _consulta;

  public EstornoController(EstornoUseCase estorno, ConsultaDeExecucaoUseCase consulta)
  {
    _estorno = estorno;
    _consulta = consulta;
  }

  [HttpPost("movimentacoes/{id:int}/estorno")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> EstornarMovimentacao(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _estorno.EstornarMovimentacao(id, usuarioId, EhPcpOuAdministrador(), ct), criado: true);
  }

  [HttpPost("montagens/{id:int}/estorno")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> EstornarMontagem(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _estorno.EstornarMontagem(id, usuarioId, EhPcpOuAdministrador(), ct), criado: true);
  }

  [HttpGet("estrutura/{id:int}/movimentacoes")]
  public async Task<IActionResult> Livro(int id, CancellationToken ct) => Traduzir(await _consulta.LivroDoNo(id, ct));
}

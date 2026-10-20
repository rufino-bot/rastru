using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Expedicao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// Os estornos dos registros compostos do fim do caminho (C7, C8 e C17 da spec da Fase 5A), cada um inteiro: a
/// remessa, a aferição, a contagem e a liberação (pedido ou resposta). O `[Authorize]` deixa passar quem pode ser autor (a Qualidade, que expede; a Gestao, que responde a
/// liberacao) e o PCP; "o autor ou PCP/Administrador" e decisao do caso de uso, que devolve 403 `Proibido` com
/// corpo, como em `EstornoController`. Controller proprio porque o conjunto de perfis e outro (decisao P7 do plano
/// de backend da Fase 5A): todo `[Authorize(Roles)]` deste arquivo espelha UMA entrada de `permissoes.ts`.
/// </summary>
[ApiController]
[Authorize]
public class EstornoDaExpedicaoController : ExecucaoControllerBase
{
  /// <summary>Spec da Fase 5A, secao 5.2: estornar e do autor, do PCP ou do Administrador.</summary>
  private const string PerfisDeEscrita = "Qualidade,Gestao,PCP,Administrador";

  private readonly RemessaUseCase _remessa;
  private readonly EstornoDaExpedicaoUseCase _estorno;

  public EstornoDaExpedicaoController(RemessaUseCase remessa, EstornoDaExpedicaoUseCase estorno)
  {
    _remessa = remessa;
    _estorno = estorno;
  }

  /// <summary>Estorna a remessa inteira (C8), inclusive a que concluiu o Pedido, que reabre (C13).</summary>
  [HttpPost("remessas/{id:int}/estorno")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> EstornarRemessa(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _remessa.Estornar(id, usuarioId, EhPcpOuAdministrador(), ct), criado: true);
  }

  /// <summary>Estorna a aferição (seção 3.4): recusado com contagem válida da Peça, no caso da não conforme.</summary>
  [HttpPost("afericoes/{id:int}/estorno")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> EstornarAfericao(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _estorno.EstornarAfericao(id, usuarioId, EhPcpOuAdministrador(), ct), criado: true);
  }

  /// <summary>Estorna a contagem inteira (C17): as duas linhas `Medicao`, de volta a `NaExpedicao`.</summary>
  [HttpPost("contagens/{id:int}/estorno")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> EstornarContagem(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _estorno.EstornarContagem(id, usuarioId, EhPcpOuAdministrador(), ct), criado: true);
  }

  /// <summary>Estorna o pedido de liberação ou a resposta (seção 3.6), o registro inteiro.</summary>
  [HttpPost("liberacoes/{id:int}/estorno")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> EstornarLiberacao(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _estorno.EstornarLiberacao(id, usuarioId, EhPcpOuAdministrador(), ct), criado: true);
  }
}

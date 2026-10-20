using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Expedicao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// Os estornos dos registros compostos do fim do caminho (C7, C8 e C17 da spec da Fase 5A), cada um inteiro: hoje,
/// a remessa. O `[Authorize]` deixa passar quem pode ser autor (a Qualidade, que expede; a Gestao, que responde a
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

  public EstornoDaExpedicaoController(RemessaUseCase remessa) => _remessa = remessa;

  /// <summary>Estorna a remessa inteira (C8), inclusive a que concluiu o Pedido, que reabre (C13).</summary>
  [HttpPost("remessas/{id:int}/estorno")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> EstornarRemessa(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _remessa.Estornar(id, usuarioId, EhPcpOuAdministrador(), ct), criado: true);
  }
}

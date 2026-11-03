using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// A Qualidade no local de expedicao (D1 da spec da Fase 5A): aferir, contar, mandar para conserto, pedir
/// liberacao a Gestao (D7; a resposta da Gestao fica em `LiberacaoController`, outro conjunto de perfis) e
/// expedir (C8, C9; o estorno da remessa fica em `EstornoDaExpedicaoController`). Um
/// controller por conjunto de perfis (decisao P7 do plano de backend da Fase 5A): todo `[Authorize(Roles)]`
/// deste arquivo espelha UMA entrada de `permissoes.ts`.
/// </summary>
[ApiController]
[Authorize]
public class ExpedicaoController : ExecucaoControllerBase
{
  /// <summary>Spec da Fase 5A, secao 5.2: o que a expedicao escreve e da Qualidade.</summary>
  private const string PerfisDeEscrita = "Qualidade,Administrador";

  private readonly AfericaoUseCase _afericao;
  private readonly LiberacaoUseCase _liberacao;
  private readonly RemessaUseCase _remessa;
  private readonly ConsultaDaExpedicaoUseCase _consulta;

  public ExpedicaoController(
      AfericaoUseCase afericao, LiberacaoUseCase liberacao, RemessaUseCase remessa, ConsultaDaExpedicaoUseCase consulta)
  {
    _afericao = afericao;
    _liberacao = liberacao;
    _remessa = remessa;
    _consulta = consulta;
  }

  [HttpPost("estrutura/{id:int}/afericoes")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Aferir(int id, [FromBody] NovaAfericaoDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _afericao.Aferir(id, dto, usuarioId, ct), criado: true);
  }

  [HttpPost("estrutura/{id:int}/contagens")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Contar(int id, [FromBody] NovaContagemDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _afericao.Contar(id, dto, usuarioId, ct), criado: true);
  }

  [HttpPost("estrutura/{id:int}/consertos")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> MandarParaConserto(int id, [FromBody] NovoConsertoDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _afericao.MandarParaConserto(id, dto, usuarioId, ct), criado: true);
  }

  [HttpPost("estrutura/{id:int}/pedidos-de-liberacao")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> PedirLiberacao(int id, [FromBody] NovoPedidoDeLiberacaoDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _liberacao.Pedir(id, dto, usuarioId, ct), criado: true);
  }

  /// <summary>Uma Peca ou um lote numa remessa so, tudo ou nada; o `201` traz os avisos de D8 (decisao P5 do plano de backend da Fase 5A).</summary>
  [HttpPost("remessas")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Expedir([FromBody] NovaRemessaDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _remessa.Expedir(dto, usuarioId, ct), criado: true);
  }

  // As leituras (secao 5.1 da spec da Fase 5A) sao de todo perfil autenticado: ler nao e escrever.

  /// <summary>A lista da Qualidade: Pedidos abertos com Peca no local de expedicao ou em conserto.</summary>
  [HttpGet("expedicao")]
  public async Task<IActionResult> Lista(CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _consulta.Lista(new QuemLe(usuarioId, EhPcpOuAdministrador()), ct));
  }

  /// <summary>O historico da Peca, mais recente primeiro (C20).</summary>
  [HttpGet("estrutura/{id:int}/expedicao")]
  public async Task<IActionResult> Historico(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _consulta.Historico(id, new QuemLe(usuarioId, EhPcpOuAdministrador()), ct));
  }

  /// <summary>As remessas dos ultimos 30 dias (decisao P9 do plano de backend da Fase 5A), onde a remessa se estorna (C20).</summary>
  [HttpGet("remessas/recentes")]
  public async Task<IActionResult> RemessasRecentes(CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _consulta.RemessasRecentes(new QuemLe(usuarioId, EhPcpOuAdministrador()), ct));
  }
}

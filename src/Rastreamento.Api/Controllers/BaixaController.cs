using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Baixa;
using Rastreamento.Application.Execucao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// A baixa (secao 5.1 da spec da Fase 5B-1): tirar quantidade de producao e leva-la a `Perdido`, com um motivo. Um
/// controller so para todas as escritas da baixa, as previas inclusive, e as leituras dela (decisao P1 do plano de
/// backend da Fase 5B-1): todo `[Authorize(Roles)]` deste arquivo espelha UMA entrada de `permissoes.ts`, `baixa`.
/// </summary>
[ApiController]
[Authorize]
public class BaixaController : ExecucaoControllerBase
{
  /// <summary>D5 da spec da Fase 5B-1: so a Qualidade e o PCP dao baixa, mais o Administrador.</summary>
  private const string PerfisDeEscrita = "Qualidade,PCP,Administrador";

  private readonly BaixaUseCase _baixa;
  private readonly ConsultaDeBaixasUseCase _consulta;

  public BaixaController(BaixaUseCase baixa, ConsultaDeBaixasUseCase consulta)
  {
    _baixa = baixa;
    _consulta = consulta;
  }

  /// <summary>Da baixa de parte do no ou de tudo dele (D3, D4, D11), na mesma `Perda`, com a Peca do topo se pedida (D13, D14).</summary>
  [HttpPost("estrutura/{id:int}/baixas")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> DarBaixa(int id, [FromBody] NovaBaixaDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _baixa.DarBaixa(id, dto, usuarioId, ct), criado: true);
  }

  /// <summary>O que a baixa gravaria, sem gravar (C14): calcula no servidor, com as mesmas recusas.</summary>
  [HttpPost("estrutura/{id:int}/baixas/previa")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> PreverBaixa(int id, [FromBody] NovaBaixaDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is null) return Unauthorized();
    return Traduzir(await _baixa.PreverBaixa(id, dto, ct));
  }

  /// <summary>O "Descartar tudo" do Agrupamento: uma baixa de tudo por `Descarte` de cada Peca dele com algo em producao.</summary>
  [HttpPost("agrupamentos/{id:int}/descarte")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> DescartarAgrupamento(int id, [FromBody] DescarteDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _baixa.DescartarAgrupamento(id, dto, usuarioId, ct), criado: true);
  }

  [HttpPost("agrupamentos/{id:int}/descarte/previa")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> PreverDescarteDoAgrupamento(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is null) return Unauthorized();
    return Traduzir(await _baixa.PreverDescarteDoAgrupamento(id, ct));
  }

  /// <summary>O "Descartar tudo" do Pedido: como o do Agrupamento, sobre todos os Agrupamentos do Pedido.</summary>
  [HttpPost("pedidos/{id:int}/descarte")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> DescartarPedido(int id, [FromBody] DescarteDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _baixa.DescartarPedido(id, dto, usuarioId, ct), criado: true);
  }

  [HttpPost("pedidos/{id:int}/descarte/previa")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> PreverDescarteDoPedido(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is null) return Unauthorized();
    return Traduzir(await _baixa.PreverDescarteDoPedido(id, ct));
  }

  /// <summary>Estorna a baixa inteira (C7), inclusive a que concluiu o Pedido, que reabre (C12). O autor, ou PCP e Administrador.</summary>
  [HttpPost("baixas/{id:int}/estorno")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> EstornarBaixa(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _baixa.Estornar(id, usuarioId, EhPcpOuAdministrador(), ct));
  }

  /// <summary>As baixas que alcancaram o no ou um descendente dele, mais recente primeiro (C27). Qualquer autenticado.</summary>
  [HttpGet("estrutura/{id:int}/baixas")]
  public async Task<IActionResult> BaixasDoNo(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _consulta.DoNo(id, new QuemLe(usuarioId, EhPcpOuAdministrador()), ct));
  }

  /// <summary>Todas as baixas do Pedido, mais recente primeiro. Qualquer autenticado.</summary>
  [HttpGet("pedidos/{id:int}/baixas")]
  public async Task<IActionResult> BaixasDoPedido(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _consulta.DoPedido(id, new QuemLe(usuarioId, EhPcpOuAdministrador()), ct));
  }
}

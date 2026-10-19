using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Separacao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// Separar material para um no, ler o historico dele e a lista do Separador (spec da Fase 4, regra 34). Controller proprio, um
/// por conjunto de perfis (decisao P6 do plano de backend da Fase 4): o estorno da separacao deixa passar
/// o PCP tambem, e vive em `EstornoDeSeparacaoController`.
/// </summary>
[ApiController]
[Authorize]
public class SeparacaoController : ExecucaoControllerBase
{
  /// <summary>Separar e do Separador; a leitura e de qualquer autenticado.</summary>
  private const string PerfisDeEscrita = "Separador,Administrador";

  private readonly SeparacaoUseCase _separacao;
  private readonly ConsultaDeSeparacaoUseCase _consulta;

  public SeparacaoController(SeparacaoUseCase separacao, ConsultaDeSeparacaoUseCase consulta)
  {
    _separacao = separacao;
    _consulta = consulta;
  }

  [HttpPost("estrutura/{id:int}/separacoes")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Separar(int id, [FromBody] SeparacaoNovaDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _separacao.Separar(id, dto, usuarioId, ct), criado: true);
  }

  /// <summary>Separar em varios nos, tudo ou nada: os mesmos perfis da rota de um no.</summary>
  [HttpPost("separacoes")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> SepararEmLote([FromBody] LoteDeSeparacaoDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _separacao.SepararEmLote(dto, usuarioId, ct), criado: true);
  }

  [HttpGet("estrutura/{id:int}/separacoes")]
  public async Task<IActionResult> Historico(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _consulta.Historico(id, new QuemLe(usuarioId, EhPcpOuAdministrador()), ct));
  }

  /// <summary>A lista do Separador: os nos de Pedido em producao com material do almoxarifado pendente.</summary>
  [HttpGet("separacoes/pendencias")]
  public async Task<IActionResult> Pendencias(CancellationToken ct) =>
      Traduzir(await _consulta.Pendencias(ct));
}

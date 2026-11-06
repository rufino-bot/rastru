using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Application.Retrabalho;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// Abrir o Pedido de Retrabalho e listar os de um Pedido (secao 4.2 da spec da Fase 5B-2). Controller proprio porque o
/// conjunto de perfis e outro (decisao P1 do plano de backend da Fase 5B-2): todo `[Authorize(Roles)]` deste arquivo
/// espelha UMA entrada de `permissoes.ts`.
/// </summary>
[ApiController]
[Authorize]
public class RetrabalhoController : ExecucaoControllerBase
{
  /// <summary>C6 da spec da Fase 5B-2: a Qualidade, o PCP e o Administrador abrem.</summary>
  private const string PerfisDeEscrita = "Qualidade,PCP,Administrador";

  private readonly RetrabalhoUseCase _retrabalho;
  private readonly CadastroDePedidoUseCase _cadastro;

  public RetrabalhoController(RetrabalhoUseCase retrabalho, CadastroDePedidoUseCase cadastro)
  {
    _retrabalho = retrabalho;
    _cadastro = cadastro;
  }

  /// <summary>
  /// Abre o Retrabalho do Pedido `{id}` (o original): 201 com o Pedido novo. O numero repetido responde o mesmo 409 do
  /// cadastro de Pedido (`ValorDuplicado`, com o campo e o Pedido que ja o tem), e o resto, `{ erro, mensagem }`.
  /// </summary>
  [HttpPost("pedidos/{id:int}/retrabalhos")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Abrir(int id, [FromBody] NovoRetrabalhoDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    var resultado = await _retrabalho.Abrir(id, dto, usuarioId, ct);
    if (resultado.TipoDoErro == TipoDeErro.Conflito && resultado.Erro == CadastroDePedidoUseCase.ErroDeNumeroDuplicado
        && await _cadastro.LocalizarDuplicado(dto.Numero ?? string.Empty, ct) is { } duplicado)
      return Conflict(CadastroControllerBase.CorpoDeDuplicado(duplicado));
    return Traduzir(resultado, criado: true);
  }

  /// <summary>Os Retrabalhos do Pedido e o numero sugerido para o proximo. Qualquer autenticado.</summary>
  [HttpGet("pedidos/{id:int}/retrabalhos")]
  public async Task<IActionResult> Listar(int id, CancellationToken ct) =>
      Traduzir(await _retrabalho.Listar(id, ct));
}

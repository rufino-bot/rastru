using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Cadastros;

namespace Rastreamento.Api.Controllers;

/// <remarks>
/// Sem `[Route]` de classe: as rotas de criacao e listagem sao aninhadas sob Pedido
/// (`/pedidos/{pedidoId}/agrupamentos`) e as de item sao de topo (`/agrupamentos/{id}`), entao
/// cada acao declara a sua.
/// </remarks>
[ApiController]
[Authorize]
public class AgrupamentosController : CadastroControllerBase
{
  private readonly CadastroDeAgrupamentoUseCase _cadastro;

  public AgrupamentosController(CadastroDeAgrupamentoUseCase cadastro) => _cadastro = cadastro;

  [HttpGet("pedidos/{pedidoId:int}/agrupamentos")]
  public async Task<IActionResult> ListarDoPedido(int pedidoId, CancellationToken ct) =>
      Ok(await _cadastro.ListarPorPedido(pedidoId, ct));

  [HttpGet("agrupamentos/{id:int}")]
  public async Task<IActionResult> Obter(int id, CancellationToken ct)
  {
    var resultado = await _cadastro.Obter(id, ct);
    return resultado.Sucesso ? Ok(resultado.Valor) : NotFound();
  }

  [HttpPost("pedidos/{pedidoId:int}/agrupamentos")]
  [Authorize(Roles = "PCP,Administrador")]
  public async Task<IActionResult> Cadastrar(
      int pedidoId, [FromBody] NovoAgrupamentoDto novo, CancellationToken ct)
  {
    var usuarioId = UsuarioDaSessao();
    if (usuarioId is null) return Unauthorized();

    var resultado = await _cadastro.Cadastrar(pedidoId, novo, usuarioId.Value, ct);
    if (resultado.Sucesso)
      return CreatedAtAction(nameof(Obter), new { id = resultado.Valor!.Id }, resultado.Valor);

    return await TraduzirFalha(
        resultado.TipoDoErro, resultado.Erro, Duplicado(pedidoId, novo.Codigo), ct);
  }

  [HttpPut("agrupamentos/{id:int}")]
  [Authorize(Roles = "PCP,Administrador")]
  public async Task<IActionResult> Editar(
      int id, [FromBody] NovoAgrupamentoDto alterado, CancellationToken ct)
  {
    var resultado = await _cadastro.Editar(id, alterado, ct);
    return resultado.Sucesso
        ? Ok(resultado.Valor)
        : await TraduzirFalha(
            resultado.TipoDoErro, resultado.Erro, DuplicadoNoPedidoDe(id, alterado.Codigo), ct);
  }

  /// <summary>
  /// Unica exclusao fisica do sistema, e guardada pelo use case: 409 com codigo
  /// (`AgrupamentoNaoVazio` / `PedidoNaoAberto`) quando a guarda barra. `TraduzirResultado`
  /// repassa o codigo como veio — quem traduz para texto e a tela.
  /// </summary>
  [HttpDelete("agrupamentos/{id:int}")]
  [Authorize(Roles = "PCP,Administrador")]
  public async Task<IActionResult> Excluir(int id, CancellationToken ct) =>
      TraduzirResultado(await _cadastro.Excluir(id, ct));

  /// <summary>
  /// Como Agrupamento pergunta pelo duplicado: por (PedidoId, Codigo) — UQ_Agrupamento_PedidoCodigo
  /// e composta. E o caso que faz a base receber um delegate em vez de um metodo de assinatura fixa.
  /// </summary>
  private LocalizadorDeDuplicado Duplicado(int pedidoId, string codigo) =>
      ct => _cadastro.LocalizarDuplicado(pedidoId, codigo, ct);

  /// <summary>
  /// Na edicao o Pedido e o do proprio Agrupamento, e nao vem da rota — dai a busca extra. Ela
  /// so acontece se houver conflito: o delegate e invocado unicamente no caminho de erro.
  /// </summary>
  private LocalizadorDeDuplicado DuplicadoNoPedidoDe(int agrupamentoId, string codigo) =>
      async ct =>
      {
        var atual = await _cadastro.Obter(agrupamentoId, ct);
        return atual.Sucesso
              ? await _cadastro.LocalizarDuplicado(atual.Valor!.PedidoId, codigo, ct)
              : null;
      };
}

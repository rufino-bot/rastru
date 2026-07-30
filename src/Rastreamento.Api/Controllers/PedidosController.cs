using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Cadastros;

namespace Rastreamento.Api.Controllers;

[ApiController]
[Route("pedidos")]
[Authorize]
public class PedidosController : CadastroControllerBase
{
    private readonly CadastroDePedidoUseCase _cadastro;

    public PedidosController(CadastroDePedidoUseCase cadastro) => _cadastro = cadastro;

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Ok(await _cadastro.Listar(ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Obter(int id, CancellationToken ct)
    {
        var resultado = await _cadastro.Obter(id, ct);
        return resultado.Sucesso ? Ok(resultado.Valor) : NotFound();
    }

    [HttpPost]
    [Authorize(Roles = "PCP,Administrador")]
    public async Task<IActionResult> Cadastrar([FromBody] NovoPedidoDto novo, CancellationToken ct)
    {
        // UsuarioDaSessao vem da base: e a unica leitura de HttpContext do cadastro de Pedido.
        var usuarioId = UsuarioDaSessao();
        if (usuarioId is null) return Unauthorized();

        var resultado = await _cadastro.Cadastrar(novo, usuarioId.Value, ct);
        if (resultado.Sucesso)
            return CreatedAtAction(nameof(Obter), new { id = resultado.Valor!.Id }, resultado.Valor);

        return await TraduzirFalha(resultado.TipoDoErro, resultado.Erro, Duplicado(novo.Numero), ct);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "PCP,Administrador")]
    public async Task<IActionResult> Editar(
        int id, [FromBody] NovoPedidoDto alterado, CancellationToken ct)
    {
        var resultado = await _cadastro.Editar(id, alterado, ct);
        return resultado.Sucesso
            ? Ok(resultado.Valor)
            : await TraduzirFalha(
                resultado.TipoDoErro, resultado.Erro, Duplicado(alterado.Numero), ct);
    }

    /// <summary>
    /// Como Pedido pergunta pelo duplicado: por numero (UQ_Pedido_Numero). O `existeInativo` que
    /// volta e sempre false — Pedido nao tem coluna `Ativo`, entao a tela nao oferece reativacao.
    /// </summary>
    private LocalizadorDeDuplicado Duplicado(string numero) =>
        ct => _cadastro.LocalizarDuplicado(numero, ct);
}

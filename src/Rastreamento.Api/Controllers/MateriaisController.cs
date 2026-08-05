using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Cadastros;

namespace Rastreamento.Api.Controllers;

[ApiController]
[Route("materiais")]
[Authorize]
public class MateriaisController : CadastroControllerBase
{
  private readonly CadastroDeMaterialUseCase _cadastro;

  public MateriaisController(CadastroDeMaterialUseCase cadastro) => _cadastro = cadastro;

  [HttpGet]
  public async Task<IActionResult> Listar(
      [FromQuery] bool incluirInativos = false, CancellationToken ct = default) =>
      Ok(await _cadastro.Listar(incluirInativos, ct));

  [HttpPost]
  [Authorize(Roles = "Administrador")]
  public async Task<IActionResult> Cadastrar([FromBody] NovoMaterialDto novo, CancellationToken ct)
  {
    var resultado = await _cadastro.Cadastrar(novo, ct);
    if (resultado.Sucesso)
      return CreatedAtAction(nameof(Listar), new { id = resultado.Valor!.Id }, resultado.Valor);

    return await TraduzirFalha(resultado.TipoDoErro, resultado.Erro, Duplicado(novo.Codigo), ct);
  }

  [HttpPut("{id:int}")]
  [Authorize(Roles = "Administrador")]
  public async Task<IActionResult> Editar(
      int id, [FromBody] NovoMaterialDto alterado, CancellationToken ct)
  {
    var resultado = await _cadastro.Editar(id, alterado, ct);
    return resultado.Sucesso
        ? Ok(resultado.Valor)
        : await TraduzirFalha(
            resultado.TipoDoErro, resultado.Erro, Duplicado(alterado.Codigo), ct);
  }

  [HttpPatch("{id:int}/ativo")]
  [Authorize(Roles = "Administrador")]
  public async Task<IActionResult> DefinirAtivo(
      int id, [FromBody] DefinirAtivoDto corpo, CancellationToken ct) =>
      TraduzirResultado(await _cadastro.DefinirAtivo(id, corpo.Ativo!.Value, ct));

  /// <summary>Como Material pergunta pelo duplicado: por codigo (UQ_Material_Codigo).</summary>
  private LocalizadorDeDuplicado Duplicado(string codigo) =>
      ct => _cadastro.LocalizarDuplicado(codigo, ct);
}

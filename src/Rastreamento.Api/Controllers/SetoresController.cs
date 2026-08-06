using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Cadastros;

namespace Rastreamento.Api.Controllers;

[ApiController]
[Route("setores")]
[Authorize]
public class SetoresController : CadastroControllerBase
{
  private readonly CadastroDeSetorUseCase _cadastro;

  public SetoresController(CadastroDeSetorUseCase cadastro) => _cadastro = cadastro;

  [HttpGet]
  public async Task<IActionResult> Listar(
      [FromQuery] bool incluirInativos = false, CancellationToken ct = default) =>
      Ok(await _cadastro.Listar(incluirInativos, ct));

  [HttpPost]
  [Authorize(Roles = "Administrador")]
  public async Task<IActionResult> Cadastrar([FromBody] NovoSetorDto novo, CancellationToken ct)
  {
    var resultado = await _cadastro.Cadastrar(novo, ct);
    if (resultado.Sucesso)
      return CreatedAtAction(nameof(Listar), new { id = resultado.Valor!.Id }, resultado.Valor);

    return await TraduzirFalha(resultado.TipoDoErro, resultado.Erro, Duplicado(novo.Nome), ct);
  }

  [HttpPut("{id:int}")]
  [Authorize(Roles = "Administrador")]
  public async Task<IActionResult> Editar(
      int id, [FromBody] NovoSetorDto alterado, CancellationToken ct)
  {
    var resultado = await _cadastro.Editar(id, alterado, ct);
    return resultado.Sucesso
        ? Ok(resultado.Valor)
        : await TraduzirFalha(resultado.TipoDoErro, resultado.Erro, Duplicado(alterado.Nome), ct);
  }

  [HttpPatch("{id:int}/ativo")]
  [Authorize(Roles = "Administrador")]
  public async Task<IActionResult> DefinirAtivo(
      int id, [FromBody] DefinirAtivoDto corpo, CancellationToken ct) =>
      TraduzirResultado(await _cadastro.DefinirAtivo(id, corpo.Ativo!.Value, ct));

  /// <summary>Como Setor pergunta pelo duplicado: por nome (UQ_Setor_Nome).</summary>
  private LocalizadorDeDuplicado Duplicado(string nome) =>
      ct => _cadastro.LocalizarDuplicado(nome, ct);
}

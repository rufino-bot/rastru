using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Cadastros;

namespace Rastreamento.Api.Controllers;

[ApiController]
[Route("componentes")]
[Authorize]
public class ComponentesController : CadastroControllerBase
{
  /// <summary>
  /// Primeira entidade de CATALOGO com dois perfis de escrita: na 1A, catalogo era so
  /// Administrador e PCP so aparecia em Pedido/Agrupamento. Decisao do usuario em 2026-08-04 —
  /// quem planeja a producao e quem conhece as pecas, e depender do Administrador para cada peca
  /// nova travaria o cadastro.
  /// </summary>
  private const string PerfisDeEscrita = "Administrador,PCP";

  private readonly CadastroDeComponenteUseCase _cadastro;

  public ComponentesController(CadastroDeComponenteUseCase cadastro) => _cadastro = cadastro;

  /// <summary>
  /// Unica falha possivel aqui e faixa de paginacao invalida (400) — por isso a traducao e direta
  /// em vez de passar pelo `TraduzirFalha`, que existe para o 409 de duplicidade. Pagina alem do
  /// fim NAO e falha: sai 200 com `itens` vazio e o `total` verdadeiro.
  /// </summary>
  [HttpGet]
  public async Task<IActionResult> Listar(
      [FromQuery] string? busca = null,
      [FromQuery] bool incluirInativos = false,
      [FromQuery] int pagina = 1,
      [FromQuery] int tamanho = CadastroDeComponenteUseCase.TamanhoDePaginaPadrao,
      CancellationToken ct = default)
  {
    var resultado = await _cadastro.Listar(busca, incluirInativos, pagina, tamanho, ct);
    return resultado.Sucesso
        ? Ok(resultado.Valor)
        : BadRequest(new { erro = resultado.Erro });
  }

  /// <summary>
  /// Detalhe de um Componente. SEM `[Authorize(Roles)]`: leitura e de qualquer autenticado, e o
  /// gate e o `[Authorize]` de classe — molde de `PedidosController.Obter`. Um componente inativo
  /// responde 200 aqui (ver o `Obter` do caso de uso).
  /// </summary>
  [HttpGet("{id:int}")]
  public async Task<IActionResult> Obter(int id, CancellationToken ct)
  {
    var resultado = await _cadastro.Obter(id, ct);
    return resultado.Sucesso ? Ok(resultado.Valor) : NotFound();
  }

  /// <summary>
  /// DIVERGENCIA DE MOLDE DELIBERADA, 2026-08-24: o `Location` do 201 aponta para `Obter`, e nao
  /// para `Listar` como em `MateriaisController` e `SetoresController`. Aqui existe
  /// `GET componentes/{id:int}`, entao `CreatedAtAction(nameof(Obter), ...)` produz o caminho do
  /// recurso criado (`/api/componentes/{id}`); la NAO existe `GET {id:int}`, e o
  /// `CreatedAtAction(nameof(Listar), new { id })` deles cai na LISTA com o id virando query
  /// string ignorada (`/api/materiais?id=123`). Os dois ficam como estao — nem poderiam mudar:
  /// `nameof(Obter)` la nao compila, porque a acao nao existe. A divergencia e de CAPACIDADE
  /// (quem tem detalhe aponta para o detalhe), nao de gosto, e some sozinha quando Material e
  /// Setor ganharem o deles.
  /// </summary>
  [HttpPost]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Cadastrar(
      [FromBody] NovoComponenteDto novo, CancellationToken ct)
  {
    var resultado = await _cadastro.Cadastrar(novo, ct);
    if (resultado.Sucesso)
      return CreatedAtAction(nameof(Obter), new { id = resultado.Valor!.Id }, resultado.Valor);

    return await TraduzirFalha(resultado.TipoDoErro, resultado.Erro, Duplicado(novo.Codigo), ct);
  }

  [HttpPut("{id:int}")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Editar(
      int id, [FromBody] NovoComponenteDto alterado, CancellationToken ct)
  {
    var resultado = await _cadastro.Editar(id, alterado, ct);
    return resultado.Sucesso
        ? Ok(resultado.Valor)
        : await TraduzirFalha(
            resultado.TipoDoErro, resultado.Erro, Duplicado(alterado.Codigo), ct);
  }

  [HttpPatch("{id:int}/ativo")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> DefinirAtivo(
      int id, [FromBody] DefinirAtivoDto corpo, CancellationToken ct) =>
      TraduzirResultado(await _cadastro.DefinirAtivo(id, corpo.Ativo!.Value, ct));

  /// <summary>Como Componente pergunta pelo duplicado: por codigo (UQ_Componente_Codigo).</summary>
  private LocalizadorDeDuplicado Duplicado(string codigo) =>
      ct => _cadastro.LocalizarDuplicado(codigo, ct);
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Common;
using Rastreamento.Application.Estrutura;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// A arvore real de um Agrupamento. Controller PROPRIO, no mesmo precedente de
/// `ReceitaPadraoController`: as acoes de criacao e leitura sao aninhadas sob Agrupamento e as de
/// no sao de topo, entao cada acao declara a propria rota, sem `[Route]` de classe.
///
/// Herda de ControllerBase e nao de CadastroControllerBase: nao ha 409 de DUPLICIDADE a montar
/// aqui (os 409 desta fase sao de ciclo, de teto e de `PedidoNaoAberto`), entao
/// TraduzirFalha/LocalizadorDeDuplicado nao serviriam para nada.
/// </summary>
[ApiController]
[Authorize]
public class EstruturaController : ControllerBase
{
  /// <summary>Mesmos perfis do Agrupamento: quem monta o pedido monta a arvore dele.</summary>
  private const string PerfisDeEscrita = "PCP,Administrador";

  private readonly MontagemDeEstruturaUseCase _montagem;

  public EstruturaController(MontagemDeEstruturaUseCase montagem) => _montagem = montagem;

  [HttpGet("agrupamentos/{agrupamentoId:int}/estrutura")]
  public async Task<IActionResult> Obter(int agrupamentoId, CancellationToken ct) =>
      Traduzir(await _montagem.ObterArvore(agrupamentoId, ct));

  [HttpPost("agrupamentos/{agrupamentoId:int}/estrutura")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> CriarPeca(
      int agrupamentoId, [FromBody] NovaPecaDto nova, CancellationToken ct) =>
      Traduzir(await _montagem.CriarPeca(agrupamentoId, nova, ct), criado: true);

  [HttpPost("estrutura/{id:int}/filhos")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> AcrescentarFilho(
      int id, [FromBody] NovoFilhoDto novo, CancellationToken ct) =>
      Traduzir(await _montagem.AcrescentarFilho(id, novo, ct), criado: true);

  [HttpPut("estrutura/{id:int}")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Editar(
      int id, [FromBody] EdicaoDeNoDto edicao, CancellationToken ct) =>
      Traduzir(await _montagem.EditarNo(id, edicao, ct));

  [HttpDelete("estrutura/{id:int}")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Excluir(int id, CancellationToken ct)
  {
    var r = await _montagem.ExcluirNo(id, ct);
    if (r.Sucesso) return NoContent();
    return Recusar(r.TipoDoErro, r.Erro, r.Detalhe);
  }

  private IActionResult Traduzir<T>(Result<T> r, bool criado = false)
  {
    if (r.Sucesso) return criado ? StatusCode(StatusCodes.Status201Created, r.Valor) : Ok(r.Valor);
    return Recusar(r.TipoDoErro, r.Erro, r.Detalhe);
  }

  /// <summary>
  /// O corpo leva `erro` (o CODIGO, por onde o front comuta) e, quando existe, `mensagem` (a FRASE
  /// que o operador le). `mensagem` e OMITIDA quando `Detalhe` e nulo — nao vira `null` no JSON.
  ///
  /// Descartar o `Detalhe` aqui e o defeito que a review da Task 3 levou um fix pass para fechar:
  /// a frase de `CicloNaReceita` NOMEIA o caminho do ciclo, e o front nao tem como reconstrui-la,
  /// porque nao sabe qual foi o caminho. `PedidoNaoAberto` continua sem frase, de proposito — e o
  /// precedente de `CadastroDeAgrupamentoUseCase`.
  /// </summary>
  private IActionResult Recusar(TipoDeErro? tipo, string? erro, string? detalhe)
  {
    if (tipo == TipoDeErro.NaoEncontrado) return NotFound();

    object corpo = detalhe is null ? new { erro } : new { erro, mensagem = detalhe };
    return tipo == TipoDeErro.Conflito ? Conflict(corpo) : BadRequest(corpo);
  }
}

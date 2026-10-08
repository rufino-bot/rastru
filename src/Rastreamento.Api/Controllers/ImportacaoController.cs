using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Arquivos;
using Rastreamento.Application.Common;
using Rastreamento.Application.Importacao;
using Rastreamento.Infrastructure.Importacao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// O rascunho do import da estrutura a partir do BOM do CAD. Controller PROPRIO, no precedente de
/// <see cref="EstruturaController"/>: as acoes de criar e listar sao aninhadas sob Agrupamento e as de
/// rascunho sao de topo, entao cada acao declara a propria rota, sem <c>[Route]</c> de classe. Leitura
/// de qualquer autenticado; escrita so de quem monta a arvore (mesmos perfis do <c>EstruturaController</c>).
///
/// Herda de <see cref="ControllerBase"/> e nao de <c>CadastroControllerBase</c> pelo mesmo motivo do
/// <c>EstruturaController</c>: nao ha 409 de duplicidade a montar aqui.
/// </summary>
[ApiController]
[Authorize]
public class ImportacaoController : ControllerBase
{
  /// <summary>Mesmos perfis da estrutura: quem monta a arvore importa o BOM dela.</summary>
  private const string PerfisDeEscrita = "PCP,Administrador";

  /// <summary>
  /// <c>[RequestSizeLimit]</c> mede o corpo multipart INTEIRO (boundary e cabecalhos da parte), nao so o
  /// arquivo: o limite do endpoint e <see cref="LeitorDeBom.TamanhoMaximoEmBytes"/> MAIS esta margem
  /// (decisao P11 do plano do import). Sem ela, um arquivo de exatos 5 MiB, que o leitor aceita, seria
  /// recusado pelo pipeline antes de chegar ao codigo. O valor e o de
  /// <c>ComponentesController.MargemDoCorpoMultipartEmBytes</c>, medido la com nome de arquivo de 260
  /// caracteres acentuados (pior caso 2.444 bytes).
  /// </summary>
  private const int MargemDoCorpoMultipartEmBytes = 4096;

  private readonly ImportacaoDeEstruturaUseCase _importacao;

  public ImportacaoController(ImportacaoDeEstruturaUseCase importacao) => _importacao = importacao;

  [HttpPost("agrupamentos/{agrupamentoId:int}/importacoes")]
  [Authorize(Roles = PerfisDeEscrita)]
  [RequestSizeLimit(LeitorDeBom.TamanhoMaximoEmBytes + MargemDoCorpoMultipartEmBytes)]
  public async Task<IActionResult> Criar(int agrupamentoId, IFormFile arquivo, CancellationToken ct)
  {
    var usuarioId = UsuarioDaSessao();
    if (usuarioId is null) return Unauthorized();

    using var memoria = new MemoryStream();
    await arquivo.CopyToAsync(memoria, ct);

    return Traduzir(
        await _importacao.Criar(agrupamentoId, arquivo.FileName, memoria.ToArray(), usuarioId.Value, ct),
        criado: true);
  }

  [HttpGet("agrupamentos/{agrupamentoId:int}/importacoes")]
  public async Task<IActionResult> Listar(int agrupamentoId, CancellationToken ct) =>
      Traduzir(await _importacao.Listar(agrupamentoId, ct));

  [HttpGet("importacoes/{id:int}")]
  public async Task<IActionResult> Obter(int id, CancellationToken ct) =>
      Traduzir(await _importacao.Obter(id, ct));

  [HttpPut("importacoes/{id:int}")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> AlterarPeca(int id, AlteracaoDaPecaDto dto, CancellationToken ct) =>
      Traduzir(await _importacao.AlterarPeca(id, dto, ct));

  [HttpPut("importacoes/{id:int}/componentes/{registroId:int}")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> AlterarComponente(
      int id, int registroId, AlteracaoDeComponenteDto dto, CancellationToken ct) =>
      Traduzir(await _importacao.AlterarComponente(id, registroId, dto, ct));

  [HttpPut("importacoes/{id:int}/filhos/{filhoId:int}")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> AlterarFilho(int id, int filhoId, AlteracaoDeFilhoDto dto, CancellationToken ct) =>
      Traduzir(await _importacao.AlterarFilho(id, filhoId, dto, ct));

  /// <summary>
  /// Solido pendente de um registro. Multipart com o arquivo e o campo <c>versao</c> (decisao P14 do plano
  /// do import). O limite do corpo e o do solido do Componente mais a mesma margem multipart, pelo mesmo
  /// motivo: <c>[RequestSizeLimit]</c> mede o corpo inteiro, e um STL de exatos 16 MiB, que o validador
  /// aceita, nao pode ser recusado antes de chegar a ele.
  /// </summary>
  [HttpPost("importacoes/{id:int}/componentes/{registroId:int}/solido")]
  [Authorize(Roles = PerfisDeEscrita)]
  [RequestSizeLimit(ValidadorDeArquivoStl.TamanhoMaximoEmBytes + MargemDoCorpoMultipartEmBytes)]
  public async Task<IActionResult> EnviarSolido(
      int id, int registroId, [FromForm] string versao, IFormFile arquivo, CancellationToken ct)
  {
    var usuarioId = UsuarioDaSessao();
    if (usuarioId is null) return Unauthorized();

    using var memoria = new MemoryStream();
    await arquivo.CopyToAsync(memoria, ct);

    return Traduzir(await _importacao.EnviarSolidoPendente(
        id, registroId, versao, arquivo.FileName, memoria.ToArray(), usuarioId.Value, ct));
  }

  /// <summary>Leitura: sem <c>Roles</c>, como o <c>GET</c> do solido do Componente. Serve o download e o visualizador.</summary>
  [HttpGet("importacoes/{id:int}/componentes/{registroId:int}/solido")]
  public async Task<IActionResult> ObterSolido(int id, int registroId, CancellationToken ct)
  {
    var r = await _importacao.ObterSolidoPendente(id, registroId, ct);
    if (!r.Sucesso) return NotFound();
    return File(r.Valor!.Conteudo, "application/octet-stream", r.Valor.NomeOriginal);
  }

  /// <summary>Reimporta: o arquivo novo no lugar do lido, mais o campo <c>versao</c>. Limite do arquivo do BOM.</summary>
  [HttpPost("importacoes/{id:int}/arquivo")]
  [Authorize(Roles = PerfisDeEscrita)]
  [RequestSizeLimit(LeitorDeBom.TamanhoMaximoEmBytes + MargemDoCorpoMultipartEmBytes)]
  public async Task<IActionResult> Reimportar(int id, [FromForm] string versao, IFormFile arquivo, CancellationToken ct)
  {
    using var memoria = new MemoryStream();
    await arquivo.CopyToAsync(memoria, ct);

    return Traduzir(await _importacao.Reimportar(id, versao, arquivo.FileName, memoria.ToArray(), ct));
  }

  [HttpDelete("importacoes/{id:int}")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Descartar(int id, CancellationToken ct)
  {
    var r = await _importacao.Descartar(id, ct);
    if (r.Sucesso) return NoContent();
    return Recusar(r.TipoDoErro, r.Erro, r.Detalhe);
  }

  /// <summary>
  /// Confirma o rascunho: grava o catalogo e cria a Peca numa transacao (secao 7 da spec do import). 201
  /// com a Peca, como o <c>POST</c> da estrutura; 400 <c>ImportacaoComBloqueios</c> sem a lista (decisao
  /// P15 do plano do import), 409 <c>ReceitaDoCatalogoMudou</c> com a frase e 409 <c>ImportacaoDesatualizada</c>.
  /// </summary>
  [HttpPost("importacoes/{id:int}/confirmacao")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Confirmar(int id, ConfirmacaoDto dto, CancellationToken ct) =>
      Traduzir(await _importacao.Confirmar(id, dto.Versao, ct), criado: true);

  /// <summary>
  /// Id do usuario da sessao, pela claim <c>sub</c> (a fronteira onde <c>HttpContext</c> para). Token
  /// assinado por nos mas sem a claim e falha de autenticacao (401), nao 500.
  /// </summary>
  private int? UsuarioDaSessao() =>
      int.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;

  private IActionResult Traduzir<T>(Result<T> r, bool criado = false)
  {
    if (r.Sucesso) return criado ? StatusCode(StatusCodes.Status201Created, r.Valor) : Ok(r.Valor);
    return Recusar(r.TipoDoErro, r.Erro, r.Detalhe);
  }

  /// <summary>
  /// O corpo leva <c>erro</c> (o CODIGO, por onde o front comuta) e, quando existe, <c>mensagem</c> (a
  /// FRASE; no <c>BomInvalido</c>, uma linha por erro do arquivo, separadas por <c>\n</c> — decisao P13).
  /// <c>mensagem</c> e OMITIDA quando <c>Detalhe</c> e nulo, e nao vira <c>null</c> no JSON.
  /// </summary>
  private IActionResult Recusar(TipoDeErro? tipo, string? erro, string? detalhe)
  {
    if (tipo == TipoDeErro.NaoEncontrado) return NotFound();

    object corpo = detalhe is null ? new { erro } : new { erro, mensagem = detalhe };
    return tipo == TipoDeErro.Conflito ? Conflict(corpo) : BadRequest(corpo);
  }
}

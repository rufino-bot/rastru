using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Arquivos;
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

  /// <summary>
  /// `[RequestSizeLimit]` mede o CORPO MULTIPART INTEIRO (boundary + cabecalhos da parte), nao so
  /// o arquivo -- por isso o limite do endpoint e `ValidadorDeArquivoStl.TamanhoMaximoEmBytes`
  /// MAIS esta margem, nao o valor cru do validador. Sem ela, um STL de exatamente 16 MiB (que o
  /// validador aceita) seria recusado pelo pipeline antes de chegar ao codigo -- medido por HTTP
  /// real em 2026-09-13: overhead de 221 bytes so para o boundary e os cabecalhos da parte com o
  /// nome "cubo.stl".
  ///
  /// <para>
  /// Medido em 2026-09-13 com o `MultipartFormDataContent` do proprio .NET (o client que
  /// `SolidoEndpointsTests` usa), variando so o `NomeOriginal` (o campo tem NVARCHAR(260) de
  /// teto): nome curto ASCII ("cubo.stl") = 218-228 bytes de overhead; nome de 260 caracteres
  /// ASCII = 722 bytes (o `MultipartFormDataContent` escreve o filename DUAS vezes --
  /// `filename=` e `filename*=utf-8''...`); nome de 260 caracteres TODOS acentuados (ç, ã, é, ú,
  /// ê, õ) = <b>2.444 bytes</b>, o pior caso medido -- para filename nao-ASCII o .NET troca
  /// `filename=` por um "encoded-word" MIME (`=?utf-8?B?...base64...?=`) e AINDA mantem o
  /// `filename*=` percent-encoded, dobrando o custo por caractere acentuado duas vezes.
  /// </para>
  ///
  /// <para>
  /// Esse e o pior caso do cliente de TESTE, nao do de producao. Medido tambem, no mesmo dia, o que
  /// um navegador envia com `fetch` + `FormData` para o mesmo nome de 260 caracteres acentuados:
  /// <b>558 bytes</b> de overhead. A margem foi dimensionada pelo maior dos dois clientes medidos.
  /// </para>
  ///
  /// <para>
  /// 4096 bytes cobrem o pior caso medido (2.444) com ~65% de folga: 16 MiB + 4096 = 16.781.312.
  /// O `[RequestSizeLimit]` SUBSTITUI, para este endpoint, o `MaxRequestBodySize` padrao do
  /// Kestrel (30.000.000 bytes) -- e o caminho que a documentacao do ASP.NET Core recomenda para
  /// mudar o limite de uma acao --, entao o padrao do Kestrel nao e teto aqui. O que continua
  /// valendo por fora do atributo e o limite de quem estiver NA FRENTE da aplicacao: sob IIS, o
  /// `maxAllowedContentLength` da filtragem de requisicao (30.000.000 bytes por padrao); atras de
  /// nginx, o `client_max_body_size` (1 MiB por padrao). Os dois sao contrato documentado, NAO
  /// medido aqui -- nao ha IIS nem nginx neste ambiente.
  /// </para>
  /// </summary>
  private const int MargemDoCorpoMultipartEmBytes = 4096;

  private readonly CadastroDeComponenteUseCase _cadastro;
  private readonly SolidoDoComponenteUseCase _solido;

  public ComponentesController(CadastroDeComponenteUseCase cadastro, SolidoDoComponenteUseCase solido)
  {
    _cadastro = cadastro;
    _solido = solido;
  }

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

  /// <summary>
  /// Envia (ou SUBSTITUI) o solido 3D do Componente. `RequestSizeLimit` usa o limite do validador
  /// MAIS `MargemDoCorpoMultipartEmBytes` (ver o comentario da constante para a conta): sem o
  /// atributo, um arquivo de 20 MiB seria lido inteiro em memoria antes de a validacao dizer que
  /// nao servia. A DECLARACAO do limite (o valor do atributo, na tabela de roteamento real) e
  /// provada por
  /// `SolidoEndpointsTests.RequestSizeLimit_do_envio_de_solido_usa_o_limite_do_validador_mais_a_margem`
  /// -- ela morre se o atributo for removido ou o valor mudar, mas nao prova o COMPORTAMENTO em
  /// producao: `WebApplicationFactory`/`TestServer`, medido em 2026-09-13, nao aplica a mesma
  /// checagem de `IHttpMaxRequestBodySizeFeature` que o Kestrel real aplica antes do model binding
  /// terminar de ler o form -- um corpo de 20 MiB passa direto ate o validador mesmo com o
  /// atributo no lugar, sob o host de teste.
  ///
  /// <para>
  /// O COMPORTAMENTO foi medido contra o Kestrel, por fora da suite, e depende do CLIENTE. Com
  /// `curl` (2026-09-13; arquivos de 17 e 31 MiB, com e sem `Expect: 100-continue`) e com um
  /// cliente Node que monta o multipart a mao, chega um 400 no formato `ValidationProblemDetails`
  /// do model binding ("Failed to read the request form. Request body too large...", chave
  /// `errors`) -- distinto do `{ erro }` que `ValidadorDeArquivoStl` devolve quando o corpo cabe na
  /// margem mas o ARQUIVO passa dos 16 MiB, e e essa distincao que mostra qual dos dois recusou.
  /// Com o `fetch` do Chromium e com o `HttpClient` do .NET (2026-09-18) NAO chega resposta: o
  /// servidor fecha a conexao com o corpo ainda subindo, e o cliente ve erro de rede. Firefox,
  /// Safari e o navegador do Android nao foram medidos, e por que os clientes diferem nao foi
  /// isolado. Logo nao ha resposta garantida a prometer acima do limite deste atributo: quem
  /// explica o tamanho ao usuario e a checagem que o front faz antes de enviar
  /// (`TAMANHO_MAXIMO_DO_SOLIDO_EM_BYTES`, em `web/src/api/cadastros.ts`).
  /// </para>
  /// </summary>
  [HttpPost("{id:int}/solido")]
  [Authorize(Roles = PerfisDeEscrita)]
  [RequestSizeLimit(ValidadorDeArquivoStl.TamanhoMaximoEmBytes + MargemDoCorpoMultipartEmBytes)]
  public async Task<IActionResult> EnviarSolido(
      int id, IFormFile arquivo, CancellationToken ct)
  {
    var usuarioId = UsuarioDaSessao();
    if (usuarioId is null) return Unauthorized();

    using var memoria = new MemoryStream();
    await arquivo.CopyToAsync(memoria, ct);

    var resultado = await _solido.Enviar(
        id, arquivo.FileName, memoria.ToArray(), usuarioId.Value, ct);

    return TraduzirResultado(resultado);
  }

  /// <summary>
  /// SEM `[Authorize(Roles)]`: leitura e de qualquer autenticado, e o gate e o `[Authorize]` de
  /// classe — molde de `Obter`. Serve o download E o viewer: um endpoint, dois consumidores.
  /// </summary>
  [HttpGet("{id:int}/solido")]
  public async Task<IActionResult> ObterSolido(int id, CancellationToken ct)
  {
    var resultado = await _solido.Obter(id, ct);
    if (!resultado.Sucesso) return NotFound();

    return File(
        resultado.Valor!.Conteudo, "application/octet-stream", resultado.Valor!.NomeOriginal);
  }

  /// <summary>Como Componente pergunta pelo duplicado: por codigo (UQ_Componente_Codigo).</summary>
  private LocalizadorDeDuplicado Duplicado(string codigo) =>
      ct => _cadastro.LocalizarDuplicado(codigo, ct);
}

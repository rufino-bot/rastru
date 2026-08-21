using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// Receita padrao de um Componente: filhos, materiais e roteiro.
///
/// Controller PROPRIO, e nao acoes novas no ComponentesController, que iria de 4 para 10 acoes. O
/// precedente de sub-recurso em controller separado ja existe: AgrupamentosController atende
/// `pedidos/{pedidoId:int}/agrupamentos` E `agrupamentos/{id:int}`, declarando o caminho completo
/// por acao em vez de um [Route] de classe. Mesmo molde aqui.
///
/// Herda de ControllerBase, e nao de CadastroControllerBase: nao ha 409 de duplicidade a montar,
/// entao TraduzirFalha/LocalizadorDeDuplicado nao servem para nada aqui — e herdar de uma base
/// so pelo parentesco convidaria acao herdada a rotear por este controller sem ninguem notar.
/// </summary>
[ApiController]
[Authorize]
public class ReceitaPadraoController : ControllerBase
{
  /// <summary>
  /// Mesmos perfis do proprio Componente: quem cadastra a peca e quem conhece a receita dela.
  /// Leitura fica liberada a qualquer autenticado, como nos outros catalogos.
  /// </summary>
  private const string PerfisDeEscrita = "Administrador,PCP";

  private readonly ReceitaPadraoUseCase _receita;

  public ReceitaPadraoController(ReceitaPadraoUseCase receita) => _receita = receita;

  // ---------------------------------------------------------------- filhos

  [HttpGet("componentes/{componenteId:int}/filhos-padrao")]
  public async Task<IActionResult> ListarFilhos(int componenteId, CancellationToken ct) =>
      Traduzir(await _receita.ListarFilhos(componenteId, ct));

  [HttpPost("componentes/{componenteId:int}/filhos-padrao")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> SubstituirFilhos(
      int componenteId, [FromBody] ReceitaDeFilhosDto corpo, CancellationToken ct) =>
      Traduzir(await _receita.SubstituirFilhos(componenteId, corpo.Linhas!, ct));

  // ---------------------------------------------------------------- materiais

  [HttpGet("componentes/{componenteId:int}/materiais-padrao")]
  public async Task<IActionResult> ListarMateriais(int componenteId, CancellationToken ct) =>
      Traduzir(await _receita.ListarMateriais(componenteId, ct));

  [HttpPost("componentes/{componenteId:int}/materiais-padrao")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> SubstituirMateriais(
      int componenteId, [FromBody] ReceitaDeMateriaisDto corpo, CancellationToken ct) =>
      Traduzir(await _receita.SubstituirMateriais(componenteId, corpo.Linhas!, ct));

  // ---------------------------------------------------------------- roteiro

  [HttpGet("componentes/{componenteId:int}/roteiro-padrao")]
  public async Task<IActionResult> ListarRoteiro(int componenteId, CancellationToken ct) =>
      Traduzir(await _receita.ListarRoteiro(componenteId, ct));

  [HttpPost("componentes/{componenteId:int}/roteiro-padrao")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> SubstituirRoteiro(
      int componenteId, [FromBody] ReceitaDeRoteiroDto corpo, CancellationToken ct) =>
      Traduzir(await _receita.SubstituirRoteiro(componenteId, corpo.Linhas!, ct));

  // ----------------------------------------------------------------

  /// <summary>
  /// 200 no sucesso, 404 para "componente pai nao existe", 409 para gravacao concorrente derrubada
  /// pelo banco, 400 para o resto.
  ///
  /// 200 e nao 201 no POST: ele nao cria um recurso novo enderecavel, substitui o conteudo de um
  /// sub-recurso que ja tem endereco. Nada de CreatedAtAction — nao ha "o" recurso criado.
  ///
  /// `switch` EXAUSTIVO sobre `TipoDeErro`, e nao um ternario com `else`: foi o `else` que, na
  /// versao anterior deste plano, engoliu `TipoDeErro.Conflito` em `BadRequest` e teria apagado,
  /// por acidente, o 409 que as Tasks 3, 4 e 5 construiram (achado C1 da review da Task 5). Um
  /// membro novo no enum tem de aparecer aqui, e nao cair em silencio no ramo generico.
  ///
  /// O `corpo.Linhas!` das acoes acima e seguro porque `[Required]` + [ApiController] barram o
  /// campo ausente com 400 ANTES de a acao rodar. Lista VAZIA continua chegando aqui: e o comando
  /// explicito de apagar a receita.
  /// </summary>
  /// <remarks>
  /// Sobre o `_`: ele cobre `Validacao` (o caso real) e `NaoAutorizado` (que estes casos de uso nao
  /// produzem — quem responde 401 e o middleware de autenticacao). Nao troque por
  /// `TipoDeErro.Validacao => ...` sem um ramo para `NaoAutorizado`: a exaustividade que importa
  /// aqui e a de LEITURA, e este paragrafo e o que impede o `else` de voltar a engolir um membro
  /// novo.
  /// </remarks>
  private IActionResult Traduzir<T>(Result<T> resultado)
  {
    if (resultado.Sucesso) return Ok(resultado.Valor);

    return resultado.TipoDoErro switch
    {
      TipoDeErro.NaoEncontrado => NotFound(new { erro = resultado.Erro }),
      TipoDeErro.Conflito => Conflict(new { erro = resultado.Erro }),
      _ => BadRequest(new { erro = resultado.Erro }),
    };
  }
}

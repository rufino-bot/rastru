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
  /// `switch` sobre `TipoDeErro` SEM braco generico, e nao um ternario com `else`: foi o `else`
  /// que, na versao anterior deste plano, engoliu `TipoDeErro.Conflito` em `BadRequest` e teria
  /// apagado, por acidente, o 409 que as Tasks 3, 4 e 5 construiram (achado C1 da review da Task
  /// 5). Todos os membros de `TipoDeErro` estao listados nominalmente — ver o bloco `remarks`
  /// abaixo para o que isso compra e por que `null` aparece na lista.
  ///
  /// O `corpo.Linhas!` das acoes acima e seguro porque `[Required]` + [ApiController] barram o
  /// campo ausente com 400 ANTES de a acao rodar. Lista VAZIA continua chegando aqui: e o comando
  /// explicito de apagar a receita.
  /// </summary>
  /// <remarks>
  /// Por que nao ha `_`: sem braco generico quem cobra membro novo e o COMPILADOR, nao um paragrafo
  /// pedindo boa vontade. MEDIDO no fix pass da review da Task 6 (achado I2), nesta arvore:
  /// acrescentei um 5o membro a `TipoDeErro` e `dotnet build Rastreamento.slnx -warnaserror` passou
  /// a FALHAR com CS8509 nomeando o membro novo e apontando este arquivo — e so este, que e o
  /// unico switch sem `_` sobre `TipoDeErro` no projeto. Com o `_` que existia antes, o mesmo
  /// experimento dava "0 Aviso(s)" e o membro novo caia calado em `BadRequest`. Sob `-warnaserror`,
  /// que e o build deste projeto, CS8509 e ERRO, nao aviso.
  ///
  /// Por que o `#pragma warning disable CS8524`: sao dois diagnosticos diferentes. CS8509 e "faltou
  /// um membro NOMEADO" — o que se quer. CS8524 e "faltou o valor NAO NOMEADO", isto e, um
  /// `(TipoDeErro)4` obtido por cast; ele dispara SEMPRE que um switch de enum nao tem `_`, entao
  /// sem o pragma o build quebrava ja sem membro novo nenhum (medido: erro CS8524 citando o padrao
  /// `(TipoDeErro)4`). Suprimir CS8524 aqui NAO enfraquece o CS8509, e isso foi medido: o
  /// experimento do 5o membro acima rodou COM o pragma no lugar. O custo do pragma — este eu nao
  /// medi, e semantica da linguagem — e que um valor sem arco correspondente passa a lancar
  /// `SwitchExpressionException` em runtime, em vez de virar 400 pelo `_`.
  ///
  /// `null` esta na lista junto de `Validacao` e `NaoAutorizado` porque `TipoDoErro` e
  /// `TipoDeErro?`, nulo quando `Sucesso` (ver `Result{T}`). E preciso dizer o que MEDI: com o
  /// pragma, apagar o `or null` compila limpo (0 avisos) — o compilador NAO cobra mais esse caso,
  /// entao quem apagar troca um `BadRequest` por uma excecao de switch sem receber nenhum aviso. O
  /// `early return` do `Sucesso` faz o `null` nao chegar aqui hoje; o arco e cinto de seguranca, e
  /// sai barato.
  ///
  /// `NaoAutorizado` divide o 400 com `Validacao` porque estes casos de uso nao o produzem: quem
  /// responde 401 e o middleware de autenticacao. Membro novo que precise de OUTRO status ganha
  /// ramo proprio; o compilador nao deixa esquecer.
  /// </remarks>
  private IActionResult Traduzir<T>(Result<T> resultado)
  {
    if (resultado.Sucesso) return Ok(resultado.Valor);

#pragma warning disable CS8524
    return resultado.TipoDoErro switch
    {
      TipoDeErro.NaoEncontrado => NotFound(new { erro = resultado.Erro }),
      TipoDeErro.Conflito => Conflict(new { erro = resultado.Erro }),
      TipoDeErro.Validacao or TipoDeErro.NaoAutorizado or null => BadRequest(new { erro = resultado.Erro }),
    };
#pragma warning restore CS8524
  }
}

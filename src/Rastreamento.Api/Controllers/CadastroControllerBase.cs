using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// O que os quatro controllers de cadastro fazem igual: traduzir <see cref="Result"/> em status
/// HTTP, montar o corpo do 409 de duplicidade e ler o usuario da sessao. Tudo <c>virtual</c> —
/// quem precisar de um desfecho diferente sobrescreve um metodo sem reescrever os outros.
/// </summary>
/// <remarks>
/// Classe abstrata nao entra na descoberta de controllers do ASP.NET, entao ela nao carrega
/// <c>[ApiController]</c> nem <c>[Route]</c>: rota e autorizacao continuam declaradas por recurso.
/// </remarks>
public abstract class CadastroControllerBase : ControllerBase
{
  /// <summary>
  /// Como ESTE recurso pergunta pelo detalhe da duplicidade. E um delegate, e nao um metodo
  /// abstrato, porque a pergunta muda de forma por entidade: `Setor` procura por nome, `Material`
  /// por codigo, `Agrupamento` por (PedidoId, Codigo). Assim cada controller fecha sobre os
  /// valores que ja tem em maos, e a base so precisa saber que da para perguntar.
  /// </summary>
  protected delegate Task<ValorDuplicadoDto?> LocalizadorDeDuplicado(CancellationToken ct);

  /// <summary>
  /// Falha de operacao que devolve valor (POST/PUT). Conflito vira 409 COM o detalhe de
  /// duplicidade — e o que permite a tela oferecer "reativar o existente" em vez de so dizer
  /// "nome em uso", ja que os indices UNIQUE nao sao filtrados por `Ativo`.
  /// </summary>
  protected virtual async Task<IActionResult> TraduzirFalha(
      TipoDeErro? tipo, string? erro, LocalizadorDeDuplicado localizar, CancellationToken ct) =>
      tipo switch
      {
        TipoDeErro.NaoEncontrado => NotFound(),
        TipoDeErro.Conflito => Conflict(await MontarConflito(localizar, erro, ct)),
        _ => BadRequest(new { erro }),
      };

  /// <summary>
  /// Corpo do 409 de duplicidade. A busca pelo duplicado acontece so aqui, no caminho de erro:
  /// o custo da segunda leitura nunca entra no caminho feliz.
  /// </summary>
  protected virtual async Task<object> MontarConflito(
      LocalizadorDeDuplicado localizar, string? erro, CancellationToken ct)
  {
    var duplicado = await localizar(ct);
    return duplicado is null
        ? new { erro }
        : new
        {
          erro = "ValorDuplicado",
          campo = duplicado.Campo,
          existeInativo = duplicado.ExisteInativo,
          idExistente = duplicado.IdExistente,
        };
  }

  /// <summary>
  /// Operacao sem valor de retorno (`PATCH /{id}/ativo`, `DELETE /agrupamentos/{id}`): 204 no
  /// sucesso. No conflito o `Erro` e repassado como veio — no DELETE de Agrupamento ele e um
  /// CODIGO ("AgrupamentoNaoVazio" / "PedidoNaoAberto"), que e o que o contrato da spec define.
  /// </summary>
  protected virtual IActionResult TraduzirResultado(Result resultado)
  {
    if (resultado.Sucesso) return NoContent();

    return resultado.TipoDoErro switch
    {
      TipoDeErro.NaoEncontrado => NotFound(),
      TipoDeErro.Conflito => Conflict(new { erro = resultado.Erro }),
      _ => BadRequest(new { erro = resultado.Erro }),
    };
  }

  /// <summary>
  /// Id do usuario da sessao, a partir da claim `sub` — a fronteira onde `HttpContext` para.
  /// `Application` recebe o valor por parametro e nunca conhece o ASP.NET. Token assinado por
  /// nos mas sem a claim e falha de autenticacao (401), nao 500 — mesmo criterio do MeController.
  /// </summary>
  protected virtual int? UsuarioDaSessao() =>
      int.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;
}

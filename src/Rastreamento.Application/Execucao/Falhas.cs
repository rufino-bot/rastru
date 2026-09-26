using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>As falhas que todo caso de uso da Fase 3 devolve igual — e o mesmo texto em todos.</summary>
internal static class Falhas
{
  public static Result<T> NaoEncontrado<T>() => Result<T>.Falha("NaoEncontrado", TipoDeErro.NaoEncontrado);

  public static Result<T> Validacao<T>(string codigo, string mensagem) =>
      Result<T>.Falha(codigo, TipoDeErro.Validacao, mensagem);

  public static Result<T> Conflito<T>(string codigo, string mensagem) =>
      Result<T>.Falha(codigo, TipoDeErro.Conflito, mensagem);

  public static Result<T> QuantidadeInvalida<T>(decimal quantidade) =>
      Validacao<T>(CodigosDaExecucao.QuantidadeInvalida,
          $"A quantidade {Quantidades.Formatar(quantidade)} não vale: tem de ser maior que zero e ter no máximo quatro casas decimais.");

  public static Result<T> PedidoFechado<T>() =>
      Conflito<T>(CodigosDaExecucao.PedidoFechado, "O Pedido deste item já foi concluído ou cancelado.");

  public static Result<T> Proibido<T>() =>
      Result<T>.Falha(CodigosDaExecucao.Proibido, TipoDeErro.Proibido,
          "Só quem fez o registro, o PCP ou o Administrador pode estorná-lo.");

  public static bool EstaFechado(PedidoDoNo? pedido) =>
      pedido is null || pedido.Status is "Concluido" or "Cancelado";

  /// <summary>
  /// A transacao da execucao, com o deadlock/lock timeout traduzido para 409. Nome diferente de
  /// `EmTransacaoAsync` de proposito: com o mesmo nome, o metodo da interface ganharia da extensao.
  /// </summary>
  public static async Task<Result<T>> ExecutarAsync<T>(
      this IExecucaoRepository execucao, Func<Task<Result<T>>> trabalho, CancellationToken ct)
  {
    try
    {
      return await execucao.EmTransacaoAsync(trabalho, ct);
    }
    catch (ConflitoDeConcorrenciaException)
    {
      return Conflito<T>(CodigosDaExecucao.ConflitoDeConcorrencia, CodigosDaExecucao.MensagemDeConflito);
    }
  }
}

internal static class NovoMovimento
{
  public static Movimentacao De(
      int estruturaItemId, string tipo, decimal quantidade, Local de, Local para, int usuarioId,
      int? montagemId = null, int? estornoDeId = null) => new()
  {
    EstruturaItemId = estruturaItemId, Tipo = tipo, Quantidade = quantidade,
    OrigemPosicao = de.Posicao, OrigemSetorId = de.SetorId, OrigemOrdem = de.Ordem,
    DestinoPosicao = para.Posicao, DestinoSetorId = para.SetorId, DestinoOrdem = para.Ordem,
    MontagemId = montagemId, EstornoDeId = estornoDeId, DataHora = DateTime.UtcNow, UsuarioId = usuarioId,
  };
}

using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Baixa;

/// <summary>
/// O estorno inteiro da baixa (secao 4.9 e C7, C9, C10, C12 da spec da Fase 5B-1), no molde de
/// `RemessaUseCase.Estornar`: uma linha `Estorno` por linha `Perda`, de `Perdido` de volta a origem dela.
/// </summary>
public sealed partial class BaixaUseCase
{
  /// <summary>
  /// Estorna a baixa inteira, os filhos que a cascata C6 levou junto inclusive. Recusas, nesta ordem: baixa
  /// inexistente (404), de outro autor sem ser PCP ou Administrador (403 `Proibido`, decidido aqui com
  /// `podeEstornarDeOutros`), Pedido `Cancelado` (409 `PedidoFechado`; o `Concluido` e aceito e reabre o que a baixa
  /// concluiu, C12) e ja estornada (409 `JaEstornado`, relido DEPOIS da trava). `Perdido` e terminal: a quantidade
  /// nunca anda depois, entao nada mais impede o estorno (C9). Recalcula o fechamento na mesma transacao (C10); o
  /// Pedido que nunca teve inicio volta a `Aberto` (C23).
  /// </summary>
  public async Task<Result<IReadOnlyList<MovimentacaoDto>>> Estornar(
      int perdaId, int usuarioId, bool podeEstornarDeOutros, CancellationToken ct)
  {
    var perda = await _baixas.ObterAsync(perdaId, ct);
    if (perda is null) return Falhas.NaoEncontrado<IReadOnlyList<MovimentacaoDto>>();
    if (perda.UsuarioId != usuarioId && !podeEstornarDeOutros) return Falhas.Proibido<IReadOnlyList<MovimentacaoDto>>();

    return await _execucao.ExecutarAsync(async () =>
    {
      // As linhas originais sao so de inclusao: lidas antes da trava so para saber quais nos travar.
      var originais = (await _baixas.ListarLinhasDasPerdasAsync([perdaId], ct))
          .Where(l => l.Tipo == TiposDeMovimentacao.Perda).ToList();
      var noIds = originais.Select(l => l.EstruturaItemId).Distinct().Order().ToList();
      if (noIds.Count == 0) return Falhas.NaoEncontrado<IReadOnlyList<MovimentacaoDto>>();
      await _execucao.TravarNosAsync(noIds, ct);
      var pedidosTravados = await _execucao.TravarPedidosDosNosAsync(noIds, ct);

      var pedido = await _execucao.ObterPedidoDoNoAsync(noIds[0], ct);
      if (pedido is null) return Falhas.NaoEncontrado<IReadOnlyList<MovimentacaoDto>>();
      if (pedido.Status == StatusCancelado)
        return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.PedidoFechado,
            $"O Pedido {pedido.Numero} foi cancelado: a baixa dele não se estorna.");

      // Relido DEPOIS da trava: dois estornos simultaneos da mesma baixa travam os mesmos nos, e o segundo ve o
      // primeiro aqui (`UX_Movimentacao_EstornoDe` e a segunda rede).
      if ((await _execucao.ListarEstornadasAsync(originais.Select(l => l.Id).ToList(), ct)).Count > 0)
        return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.JaEstornado, "Esta baixa já foi estornada.");

      var agora = DateTime.UtcNow;
      var estornos = originais.Select(l =>
      {
        var estorno = NovoMovimento.De(
            l.EstruturaItemId, TiposDeMovimentacao.Estorno, l.Quantidade, Local.Perdido, Local.DaOrigem(l), usuarioId,
            estornoDeId: l.Id);
        estorno.PerdaId = perdaId;
        estorno.DataHora = agora;
        return estorno;
      }).ToList();
      foreach (var estorno in estornos) _execucao.Adicionar(estorno);
      await _execucao.SalvarAlteracoesAsync(ct);
      await _fechamento.RecalcularAsync(pedidosTravados, agora, ct);
      return Result<IReadOnlyList<MovimentacaoDto>>.Ok(await _projetor.ProjetarMovimentacoesAsync(estornos, ct));
    }, ct);
  }
}

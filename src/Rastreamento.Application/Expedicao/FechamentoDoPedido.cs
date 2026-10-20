using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Expedicao;

/// <summary>
/// Recalcula do zero o status e o fechamento dos Pedidos (C11, C12, C13 e C16 da spec da Fase 5A), dentro da
/// transacao do movimento que pode te-los mudado. Contrato: quem chama ja travou os Pedidos
/// (`TravarPedidosDosNosAsync`, depois dos nos) e ja salvou os movimentos; aqui se le o status por
/// `TravarPedidoAsync` (a trava ja e da transacao, nao espera), as Pecas e os saldos delas, e se grava o
/// resultado de <see cref="CalculoDoFechamento.Calcular"/>. Nenhum no e travado.
/// </summary>
internal sealed class FechamentoDoPedido(IExecucaoRepository execucao)
{
  public async Task RecalcularAsync(IReadOnlyCollection<int> pedidoIds, DateTime agora, CancellationToken ct)
  {
    var ids = pedidoIds.Distinct().Order().ToList();
    if (ids.Count == 0) return;

    var status = new Dictionary<int, string>();
    foreach (var id in ids)
      if (await execucao.TravarPedidoAsync(id, ct) is { } pedido) status[id] = pedido.Status;

    var pecas = await execucao.ListarPecasDosPedidosAsync(status.Keys.ToList(), ct);
    var saldos = (await execucao.ListarSaldosAsync(pecas.Select(p => p.PecaId).ToList(), ct))
        .GroupBy(s => s.EstruturaItemId)
        .ToDictionary(g => g.Key, g => CalculoDaExpedicao.Posicoes(
            g.Select(s => (new Local(s.Posicao, s.SetorId, s.Ordem), s.Quantidade))));

    foreach (var (pedidoId, statusAtual) in status)
    {
      var doPedido = pecas.Where(p => p.PedidoId == pedidoId)
          .Select(p => NoFechamento(p, saldos.GetValueOrDefault(p.PecaId)))
          .ToList();
      var fechamento = CalculoDoFechamento.Calcular(pedidoId, statusAtual, doPedido);
      // `Aberto`, `Cancelado` e o Pedido sem Peca (C16) voltam iguais e sem Agrupamento: nada a gravar.
      if (fechamento.Status == statusAtual && fechamento.Agrupamentos.Count == 0) continue;
      await execucao.GravarFechamentoAsync(fechamento, agora, ct);
    }
  }

  private static PecaNoFechamento NoFechamento(PecaDoPedido p, PosicoesDaPeca? posicoes) =>
      new(p.PecaId, p.AgrupamentoId, p.Quantidade, posicoes?.Expedido ?? 0m, posicoes?.NoLocalDeExpedicao ?? 0m);
}

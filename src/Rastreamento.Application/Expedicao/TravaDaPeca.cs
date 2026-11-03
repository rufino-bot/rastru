using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Expedicao;

/// <summary>A Peca travada e os Pedidos travados com ela (vazio quando nao se travou Pedido), para o fechamento.</summary>
internal sealed record PecaTravada(EstruturaItem No, IReadOnlyList<int> Pedidos);

/// <summary>
/// O comeco comum das escritas da expedicao, dentro da transacao da execucao: trava o no; 404 se nao existe;
/// trava o Pedido dele (so a Peca, e so quando `travarPedido`) ANTES da primeira leitura do Pedido, que a trava
/// converteria de S para U e deadlockaria (`PK_Pedido`); recusa Pedido fechado; recusa Item (so a Peca chega ao
/// local de expedicao, regra 29). A trava da Peca e o que serializa as escritas do mesmo fim do caminho.
/// </summary>
internal static class TravaDaPeca
{
  public static async Task<(PecaTravada? Peca, Result<T>? Recusa)> TravarAsync<T>(
      IExecucaoRepository execucao, LeitorDeEstado leitor, int pecaId, bool travarPedido, CancellationToken ct)
  {
    var no = (await execucao.TravarNosAsync([pecaId], ct)).SingleOrDefault();
    if (no is null) return (null, Falhas.NaoEncontrado<T>());
    var ehPeca = no.EstruturaPaiId is null;
    var pedidos = travarPedido && ehPeca ? await execucao.TravarPedidosDosNosAsync([pecaId], ct) : [];

    if (Falhas.EstaFechado(await execucao.ObterPedidoDoNoAsync(pecaId, ct)))
      return (null, Falhas.PedidoFechado<T>());
    if (!ehPeca)
    {
      var nome = (await leitor.CarregarAsync([no], ct)).Nome(pecaId);
      return (null, Falhas.Validacao<T>(CodigosDaExpedicao.NaoEhPeca,
          $"{nome} é um Item: só a Peça chega ao local de expedição."));
    }
    return (new PecaTravada(no, pedidos), null);
  }
}

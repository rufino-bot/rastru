using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;

namespace Rastreamento.Application.Retrabalho;

/// <summary>A lista dos Retrabalhos de um Pedido e o numero sugerido para o proximo (secao 6 da spec da Fase 5B-2).</summary>
public sealed partial class RetrabalhoUseCase
{
  /// <summary>
  /// Os Retrabalhos que tem o Pedido como origem, do mais antigo ao mais novo, com a origem de cada um (a baixa, a
  /// reclamacao ou nenhuma), e o numero sugerido (decisao P7 do plano de backend da Fase 5B-2): `{numero}-R{k}`, o menor
  /// k a partir de 1 que nenhum Pedido usa, com o numero do original cortado quando passaria de 30 (`NumeroDoRetrabalho`). 404 se o Pedido nao existe.
  /// </summary>
  public Task<Result<RetrabalhosDoPedidoDto>> Listar(int pedidoId, CancellationToken ct) =>
      _execucao.ConsultarAsync(async () =>
      {
        var pedido = await _pedidos.ObterPorIdAsync(pedidoId, ct);
        if (pedido is null) return Falhas.NaoEncontrado<RetrabalhosDoPedidoDto>();

        var retrabalhos = (await _reposicao.ListarRetrabalhosAsync(pedidoId, ct))
            .Select(r => new RetrabalhoResumoDto(
                r.Pedido.Id, r.Pedido.Numero, r.Pedido.MotivoRetrabalho, r.Pedido.Status, r.Pedido.DataEntrega,
                r.PerdaId is int perda ? new OrigemDoRetrabalhoDto("Baixa", perda)
                : r.ReclamacaoId is int reclamacao ? new OrigemDoRetrabalhoDto("Reclamacao", reclamacao)
                : new OrigemDoRetrabalhoDto("Avulsa", null)))
            .ToList();
        return Result<RetrabalhosDoPedidoDto>.Ok(new RetrabalhosDoPedidoDto(await NumeroSugeridoAsync(pedido.Numero, ct), retrabalhos));
      }, ct);

  private async Task<string> NumeroSugeridoAsync(string numero, CancellationToken ct) =>
      NumeroDoRetrabalho.Sugerir(numero, await _pedidos.ListarNumerosQueComecamComAsync(NumeroDoRetrabalho.PrefixoDaConsulta(numero), ct));
}

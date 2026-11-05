using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Baixa;

/// <summary>
/// As leituras da baixa (secao 5.2 da spec da Fase 5B-1): as de um no e as de um Pedido, mais recente primeiro. Todo
/// metodo passa por `ConsultarAsync`, como as da execucao e da expedicao, e projeta em lote pelo mesmo
/// `BaixaUseCase.ProjetarAsync` do `201` de quem grava.
/// </summary>
public sealed class ConsultaDeBaixasUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly IBaixaRepository _baixas;
  private readonly BaixaUseCase _baixa;

  public ConsultaDeBaixasUseCase(IExecucaoRepository execucao, IBaixaRepository baixas, BaixaUseCase baixa)
  {
    _execucao = execucao;
    _baixas = baixas;
    _baixa = baixa;
  }

  /// <summary>
  /// As baixas com ao menos uma linha no no ou num descendente dele (C27): a baixa de um filho aparece na Peca, e o
  /// "Descartar tudo" do Agrupamento e do Pedido aparece em todo no que levou. 404 se o no nao existe.
  /// </summary>
  public Task<Result<IReadOnlyList<BaixaDto>>> DoNo(int noId, QuemLe quem, CancellationToken ct) =>
      _execucao.ConsultarAsync(async () =>
      {
        var arvore = await _baixas.ListarSubarvoreAsync(noId, ct);
        if (arvore.Count == 0) return Falhas.NaoEncontrado<IReadOnlyList<BaixaDto>>();
        var perdas = await _baixas.ListarPerdasDosNosAsync(arvore, ct);
        return Result<IReadOnlyList<BaixaDto>>.Ok(await _baixa.ProjetarAsync(perdas, quem, ct));
      }, ct);

  /// <summary>Todas as baixas do Pedido. 404 se o Pedido nao existe.</summary>
  public Task<Result<IReadOnlyList<BaixaDto>>> DoPedido(int pedidoId, QuemLe quem, CancellationToken ct) =>
      _execucao.ConsultarAsync(async () =>
      {
        if (!(await _baixas.ListarStatusDosPedidosAsync([pedidoId], ct)).ContainsKey(pedidoId))
          return Falhas.NaoEncontrado<IReadOnlyList<BaixaDto>>();
        var perdas = await _baixas.ListarPerdasDoPedidoAsync(pedidoId, ct);
        return Result<IReadOnlyList<BaixaDto>>.Ok(await _baixa.ProjetarAsync(perdas, quem, ct));
      }, ct);
}

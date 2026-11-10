using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Retrabalho;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Baixa;

/// <summary>
/// O estorno da baixa `Reaproveitado` (secao 4.4 da spec da Fase 5B-2): inteiro, numa transacao, desfaz as tres coisas
/// que o "Usar a sobra" gravou — a baixa da sobra no original, os `Pronto` no Retrabalho que a apontam e a baixa ligada
/// da cascata do pronto (C18; decisao P2 do plano de backend da Fase 5B-2).
/// </summary>
public sealed partial class BaixaUseCase
{
  /// <summary>As linhas e os nos a travar do reaproveitamento, lidos fora da transacao (so de inclusao).</summary>
  private sealed record LidoDoReaproveitamento(
      IReadOnlyList<Movimentacao> DaSobra, IReadOnlyList<Movimentacao> Prontos, IReadOnlyList<Movimentacao> DaLigada,
      IReadOnlyList<int> NosATravar);

  /// <summary>
  /// Trava o mesmo que o "Usar a sobra" (decisao P6 do plano de backend da Fase 5B-2): a arvore da Peca de origem e os
  /// nos do Retrabalho, em ordem crescente de Id, e depois os dois Pedidos. Recusas, depois das travas: um dos Pedidos
  /// `Cancelado` (409 `PedidoFechado`), ja estornada (409 `JaEstornado`) e o pronto que andou (409 `ProntoJaAndou`):
  /// o saldo do no do Retrabalho, na posicao em que os `Pronto` desta baixa puseram a sobra, ficou abaixo do que eles
  /// puseram. A baixa ligada leva a `Perdido` (terminal, ela nunca anda) o "a iniciar" e o pronto de um uso anterior: o pronto do
  /// primeiro uso, com o segundo valendo, ja nao esta todo nas posicoes em que ele o pos, e e essa a recusa. Recalcula o fechamento
  /// dos dois Pedidos na mesma transacao.
  /// </summary>
  private async Task<Result<IReadOnlyList<MovimentacaoDto>>> EstornarReaproveitamentoAsync(
      Perda perda, int usuarioId, CancellationToken ct)
  {
    // Fora da transacao, como no "Usar a sobra": as linhas sao so de inclusao, e as arvores so dizem o que travar.
    var lido = await _execucao.ConsultarAsync(() => LerReaproveitamentoAsync(perda, ct), ct);
    if (!lido.Sucesso) return Falhas.Repassar<IReadOnlyList<MovimentacaoDto>, LidoDoReaproveitamento>(lido);
    var (daSobra, prontos, daLigada, nosATravar) = lido.Valor!;

    return await _execucao.ExecutarAsync(async () =>
    {
      var nos = await _execucao.TravarNosAsync(nosATravar, ct);
      var pedidosTravados = await _execucao.TravarPedidosDosNosAsync(nosATravar, ct);
      foreach (var pedidoId in pedidosTravados)
        if (await _execucao.TravarPedidoAsync(pedidoId, ct) is { Status: StatusCancelado } cancelado)
          return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.PedidoFechado,
              $"O Pedido {cancelado.Numero} foi cancelado: o reaproveitamento da sobra não se estorna.");

      if ((await _execucao.ListarEstornadasAsync(daSobra.Select(l => l.Id).ToList(), ct)).Count > 0)
        return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.JaEstornado, "Esta baixa já foi estornada.");

      var idsComPronto = prontos.Select(p => p.EstruturaItemId).ToHashSet();
      var estado = await _leitor.CarregarAsync(nos.Where(n => idsComPronto.Contains(n.Id)).ToList(), ct);
      foreach (var porPosicao in prontos.GroupBy(p => (p.EstruturaItemId, Local: Local.DoDestino(p))))
      {
        var (noId, local) = porPosicao.Key;
        var posto = porPosicao.Sum(p => p.Quantidade);
        var ali = estado.Calc.Saldo(noId, local);
        if (ali < posto)
          return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDoRetrabalho.ProntoJaAndou,
              $"Só há {Quantidades.Formatar(Math.Max(0m, ali))} de {estado.Nome(noId)} onde o pronto pôs "
              + $"{Quantidades.Formatar(posto)}: a quantidade já andou no Retrabalho.");
      }

      var agora = DateTime.UtcNow;
      var estornos = daSobra.Concat(prontos).Concat(daLigada).Select(l =>
      {
        var estorno = NovoMovimento.De(
            l.EstruturaItemId, TiposDeMovimentacao.Estorno, l.Quantidade, Local.DoDestino(l), Local.DaOrigem(l), usuarioId,
            estornoDeId: l.Id);
        estorno.PerdaId = l.PerdaId;
        estorno.DataHora = agora;
        return estorno;
      }).ToList();
      foreach (var estorno in estornos) _execucao.Adicionar(estorno);
      await _execucao.SalvarAlteracoesAsync(ct);
      await _fechamento.RecalcularAsync(pedidosTravados, agora, ct);
      return Result<IReadOnlyList<MovimentacaoDto>>.Ok(await _projetor.ProjetarMovimentacoesAsync(estornos, ct));
    }, ct);
  }

  /// <summary>
  /// As linhas da baixa (`Perda`, no original), os `Pronto` que a apontam (no Retrabalho) e as linhas da baixa ligada;
  /// e os nos a travar: as arvores da Peca de origem e das Pecas do Retrabalho, mais os nos das linhas.
  /// </summary>
  private async Task<Result<LidoDoReaproveitamento>> LerReaproveitamentoAsync(Perda perda, CancellationToken ct)
  {
    var todas = await _baixas.ListarLinhasDasPerdasAsync([perda.Id], ct);
    var daSobra = todas.Where(l => l.Tipo == TiposDeMovimentacao.Perda).ToList();
    if (daSobra.Count == 0) return Falhas.NaoEncontrado<LidoDoReaproveitamento>();
    var prontos = todas.Where(l => l.Tipo == TiposDeMovimentacao.Pronto).ToList();
    var ligadas = await _baixas.ListarPerdasLigadasAsync(perda.Id, ct);
    var daLigada = ligadas.Count == 0
        ? []
        : (await _baixas.ListarLinhasDasPerdasAsync(ligadas.Select(p => p.Id).ToList(), ct))
            .Where(l => l.Tipo == TiposDeMovimentacao.Perda).ToList();

    var nos = new HashSet<int>(daSobra.Concat(prontos).Concat(daLigada).Select(l => l.EstruturaItemId));
    if (perda.EstruturaItemId is int pecaDeOrigem) nos.UnionWith(await _execucao.ListarIdsDaSubarvoreAsync(pecaDeOrigem, ct));
    if (perda.PedidoRetrabalhoId is int retrabalhoId)
      foreach (var peca in await _execucao.ListarPecasDosPedidosAsync([retrabalhoId], ct))
        nos.UnionWith(await _execucao.ListarIdsDaSubarvoreAsync(peca.PecaId, ct));

    return Result<LidoDoReaproveitamento>.Ok(new LidoDoReaproveitamento(daSobra, prontos, daLigada, nos.Order().ToList()));
  }
}

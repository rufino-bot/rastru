using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Expedicao;

/// <summary>
/// Uma Peca do Pedido no que o fechamento precisa: a quantidade do no, o expedido e o que esta no local de
/// expedicao (<see cref="PosicoesDaPeca.NoLocalDeExpedicao"/>, as cinco posicoes; o conserto nao conta).
/// </summary>
public sealed record PecaNoFechamento(
    int PecaId, int AgrupamentoId, decimal Quantidade, decimal Expedido, decimal NoLocalDeExpedicao);

/// <summary>
/// O status do Pedido e o fechamento pela regra 13 (C11, C12, C13 e C16 da spec da Fase 5A), puro: quem le
/// as Pecas e grava o resultado e o servico do fechamento. Recalcular do zero a cada escrita e o que faz o
/// estorno de uma remessa reabrir o que ela concluiu (C13) sem regra propria.
/// </summary>
public static class CalculoDoFechamento
{
  // Os valores de `CK_Pedido_Status`.
  private const string Aberto = "Aberto";
  private const string EmProducao = "EmProducao";
  private const string AguardandoExpedicao = "AguardandoExpedicao";
  private const string Concluido = "Concluido";
  private const string Cancelado = "Cancelado";

  /// <summary>
  /// A Peca conclui com tudo expedido; o Agrupamento, com todas as Pecas dele; o Pedido, com todos os
  /// Agrupamentos que tem Peca (C16: so os que aparecem em <paramref name="pecas"/>). Sem concluir,
  /// `AguardandoExpedicao` quando todo o nao expedido de cada Peca esta no local de expedicao (C11), senao
  /// `EmProducao`.
  /// <para>
  /// Nao mudam: o Pedido `Aberto` (o status so sai de `Aberto` pelo primeiro inicio, e nunca volta a ele),
  /// o `Cancelado` (fica fora do fechamento) e o Pedido sem Peca nenhuma (C16: nao conclui). Nesses casos o
  /// status volta igual e `Agrupamentos` vem vazio, para o repositorio nao tocar em nenhum.
  /// </para>
  /// </summary>
  public static FechamentoCalculado Calcular(int pedidoId, string statusAtual, IReadOnlyList<PecaNoFechamento> pecas)
  {
    if (statusAtual is Aberto or Cancelado || pecas.Count == 0)
      return new FechamentoCalculado(pedidoId, statusAtual, statusAtual == Concluido, []);

    var agrupamentos = pecas
        .GroupBy(p => p.AgrupamentoId)
        .OrderBy(g => g.Key)
        .Select(g => new AgrupamentoNoFechamento(g.Key, g.All(PecaConcluida)))
        .ToList();

    var status = agrupamentos.All(a => a.Concluido) ? Concluido
        : pecas.All(TodoONaoExpedidoNoLocalDeExpedicao) ? AguardandoExpedicao
        : EmProducao;

    return new FechamentoCalculado(pedidoId, status, status == Concluido, agrupamentos);
  }

  private static bool PecaConcluida(PecaNoFechamento p) => p.Expedido >= p.Quantidade;

  private static bool TodoONaoExpedidoNoLocalDeExpedicao(PecaNoFechamento p) =>
      p.NoLocalDeExpedicao >= p.Quantidade - p.Expedido;
}

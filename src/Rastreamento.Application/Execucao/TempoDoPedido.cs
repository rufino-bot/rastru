using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// O tempo do Pedido (spec da Fase 6A, C17, e regra 14 de `specs/01`): o inicio real e a pausa depois dele.
/// Funcao pura: nao le banco.
/// </summary>
public static class TempoDoPedido
{
  /// <summary>
  /// A menor `DataHora` entre as linhas `Inicio` e `Devolucao` nao estornadas; nulo sem nenhuma. `Pronto` nao conta
  /// (nao e producao de verdade).
  /// </summary>
  public static DateTime? InicioReal(IReadOnlyList<Movimentacao> linhasDoPedido)
  {
    var estornadas = linhasDoPedido.Where(l => l.EstornoDeId is not null).Select(l => l.EstornoDeId!.Value).ToHashSet();
    DateTime? menor = null;
    foreach (var l in linhasDoPedido)
    {
      if (l.Tipo != TiposDeMovimentacao.Inicio && l.Tipo != TiposDeMovimentacao.Devolucao) continue;
      if (estornadas.Contains(l.Id)) continue;
      
      if (menor is null || l.DataHora < menor) menor = l.DataHora;
    }
    return menor;
  }

  /// <summary>A soma de cada intervalo `PausadoEm` ate `RetomadoEm` (ou `agora`, se aberto), recortado a partir do inicio.</summary>
  public static TimeSpan PausadoDepoisDe(DateTime inicio, IReadOnlyList<PedidoPausa> pausas, DateTime agora)
  {
    var total = TimeSpan.Zero;
    foreach (var p in pausas)
    {
      var de = p.PausadoEm < inicio ? inicio : p.PausadoEm;
      var ate = p.RetomadoEm ?? agora;
      if (ate > de) total += ate - de;
    }
    return total;
  }
}

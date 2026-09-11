using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IAgrupamentoRepository
{
  /// <summary>Entidade RASTREADA: `Editar` e `Excluir` contam com o change tracking.</summary>
  Task<Agrupamento?> ObterPorIdAsync(int id, CancellationToken ct);

  /// <summary>
  /// Duplicidade e por (PedidoId, Codigo) — UQ_Agrupamento_PedidoCodigo e composta, entao o
  /// mesmo codigo pode existir uma vez em cada Pedido.
  /// </summary>
  Task<Agrupamento?> ObterPorPedidoECodigoAsync(int pedidoId, string codigo, CancellationToken ct);

  Task<IReadOnlyList<Agrupamento>> ListarPorPedidoAsync(int pedidoId, CancellationToken ct);

  Task AdicionarAsync(Agrupamento agrupamento, CancellationToken ct);

  /// <summary>Hard delete — permitido so pela guarda do use case (vazio + Pedido Aberto).</summary>
  Task RemoverAsync(Agrupamento agrupamento, CancellationToken ct);

  /// <summary>
  /// Existe alguma EstruturaItem apontando para este Agrupamento? E a guarda do DELETE.
  /// Ate a Fase 2 isto era SQL direto, porque `EstruturaItem` nao tinha entidade mapeada; a Fase 2
  /// mapeou, e a implementacao virou LINQ sem o contrato mudar — que era exatamente o previsto.
  /// </summary>
  Task<bool> TemEstruturaAsync(int agrupamentoId, CancellationToken ct);

  Task SalvarAlteracoesAsync(CancellationToken ct);
}

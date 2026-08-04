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
  /// `EstruturaItem` e tabela da FASE 2 e ainda nao tem entidade mapeada de proposito — mapear
  /// aqui puxaria a Fase 2 para dentro da Fase 1, e um mapeamento feito so para servir de guarda
  /// envelheceria errado. A implementacao usa SQL direto; quando a Fase 2 mapear a entidade,
  /// isto vira LINQ e o contrato nao muda.
  /// </summary>
  Task<bool> TemEstruturaAsync(int agrupamentoId, CancellationToken ct);

  Task SalvarAlteracoesAsync(CancellationToken ct);
}

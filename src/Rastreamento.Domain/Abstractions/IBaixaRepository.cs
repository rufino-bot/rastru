using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// A baixa (spec da Fase 5B-1, secao 3.2): o registro `dbo.Perda` e as linhas do livro que o apontam, e a arvore que a
/// conta da baixa precisa ler. Toda leitura devolve dado SOLTO (sem change tracking); a escrita e `Adicionar`, e o
/// repositorio NAO tem `Salvar` proprio — quem persiste e o <see cref="IExecucaoRepository.SalvarAlteracoesAsync"/>,
/// porque o `DbContext` e o mesmo (escopado) e a baixa nasce na mesma transacao das linhas do livro. A `Perda` e so
/// de inclusao: nao existe atualizacao nem exclusao aqui, e "estornada" e sempre calculado (existe estorno apontando).
/// </summary>
public interface IBaixaRepository
{
  void Adicionar(Perda perda);

  Task<Perda?> ObterAsync(int id, CancellationToken ct);

  /// <summary>As linhas do livro das baixas pedidas, com os estornos delas (o estorno aponta a mesma baixa), em ordem de Id.</summary>
  Task<IReadOnlyList<Movimentacao>> ListarLinhasDasPerdasAsync(IReadOnlyCollection<int> perdaIds, CancellationToken ct);

  /// <summary>O proprio no e todos os descendentes, em ordem de Id; vazio se o no nao existe.</summary>
  Task<IReadOnlyList<int>> ListarSubarvoreAsync(int noId, CancellationToken ct);

  /// <summary>
  /// Os ancestrais do no, do PAI ate a Peca do topo, nessa ordem; vazio quando o no e Peca ou nao existe.
  /// </summary>
  Task<IReadOnlyList<int>> ListarCadeiaAteOTopoAsync(int noId, CancellationToken ct);

  /// <summary>O status de AGORA de cada Pedido pedido, por Id. Pedido inexistente nao volta.</summary>
  Task<IReadOnlyDictionary<int, string>> ListarStatusDosPedidosAsync(IReadOnlyCollection<int> pedidoIds, CancellationToken ct);

  /// <summary>
  /// As Pecas (nos sem pai) do escopo do "Descartar tudo", em ordem de Id: as do Agrupamento, ou, sem ele, as de todos
  /// os Agrupamentos do Pedido.
  /// </summary>
  Task<IReadOnlyList<int>> ListarPecasDoEscopoAsync(int pedidoId, int? agrupamentoId, CancellationToken ct);

  /// <summary>O Pedido do Agrupamento, ou nulo se o Agrupamento nao existe.</summary>
  Task<int?> ObterPedidoDoAgrupamentoAsync(int agrupamentoId, CancellationToken ct);
}

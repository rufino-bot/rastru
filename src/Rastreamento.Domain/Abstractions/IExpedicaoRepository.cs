using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Os registros do fim do caminho da Peca (spec da Fase 5A, secao 3): afericoes, contagens, liberacoes e
/// remessas, e as linhas do livro que apontam cada um. Toda leitura devolve dado SOLTO (sem change tracking);
/// as escritas sao `Adicionar`, e o repositorio NAO tem `Salvar` proprio — quem persiste e o
/// <see cref="IExecucaoRepository.SalvarAlteracoesAsync"/>, porque o `DbContext` e o mesmo (escopado) e os
/// registros nascem na mesma transacao das linhas do livro. Os quatro registros sao so de inclusao: nao existe
/// atualizacao nem exclusao aqui, e "estornado" e sempre calculado (existe estorno apontando).
/// </summary>
public interface IExpedicaoRepository
{
  void Adicionar(RelatorioDimensional afericao);

  void Adicionar(ContagemDimensional contagem);

  void Adicionar(LiberacaoDimensional liberacao);

  void Adicionar(Remessa remessa);

  /// <summary>Afericoes e estornos de afericao das Pecas pedidas, em ordem de Id.</summary>
  Task<IReadOnlyList<RelatorioDimensional>> ListarAfericoesAsync(IReadOnlyCollection<int> pecaIds, CancellationToken ct);

  Task<RelatorioDimensional?> ObterAfericaoAsync(int id, CancellationToken ct);

  Task<IReadOnlyList<ContagemDimensional>> ListarContagensAsync(IReadOnlyCollection<int> pecaIds, CancellationToken ct);

  Task<ContagemDimensional?> ObterContagemAsync(int id, CancellationToken ct);

  /// <summary>Pedidos e respostas de liberacao das Pecas pedidas, em ordem de Id.</summary>
  Task<IReadOnlyList<LiberacaoDimensional>> ListarLiberacoesAsync(IReadOnlyCollection<int> pecaIds, CancellationToken ct);

  /// <summary>
  /// Todo registro `Tipo = Pedido` cuja Peca vive num Pedido que nao esta `Concluido` nem `Cancelado`. Se cada
  /// um ja foi respondido ou estornado (pendente ou nao), decide o caso de uso.
  /// </summary>
  Task<IReadOnlyList<LiberacaoDimensional>> ListarPedidosDeLiberacaoAsync(CancellationToken ct);

  Task<LiberacaoDimensional?> ObterLiberacaoAsync(int id, CancellationToken ct);

  Task<Remessa?> ObterRemessaAsync(int id, CancellationToken ct);

  /// <summary>As remessas de `DataHora` a partir de `desdeUtc` (inclusive), da mais recente para a mais antiga.</summary>
  Task<IReadOnlyList<Remessa>> ListarRemessasDesdeAsync(DateTime desdeUtc, CancellationToken ct);

  // As linhas do livro de cada registro, com os estornos delas (o estorno aponta o mesmo registro).

  Task<IReadOnlyList<Movimentacao>> ListarLinhasDasContagensAsync(IReadOnlyCollection<int> contagemIds, CancellationToken ct);

  Task<IReadOnlyList<Movimentacao>> ListarLinhasDasLiberacoesAsync(IReadOnlyCollection<int> liberacaoIds, CancellationToken ct);

  Task<IReadOnlyList<Movimentacao>> ListarLinhasDasRemessasAsync(IReadOnlyCollection<int> remessaIds, CancellationToken ct);

  /// <summary>Das afericoes pedidas, as que ja tem um Estorno apontando para elas.</summary>
  Task<IReadOnlySet<int>> ListarAfericoesEstornadasAsync(IReadOnlyCollection<int> afericaoIds, CancellationToken ct);
}

using Rastreamento.Application.Baixa;
using Rastreamento.Application.Retrabalho;
using Rastreamento.Application.Tests.Baixa;
using Rastreamento.Application.Tests.Estrutura;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Retrabalho;

/// <summary>
/// Fake de <see cref="IReposicaoRepository"/> para os testes que nao tratam da reposicao: nao ha reclamacao, baixa de perda
/// nem dispensa, entao o a repor e zero e nada aponta Retrabalho. Os testes de reposicao rodam na Api, contra o banco.
/// </summary>
public sealed class FakeReposicaoRepo : IReposicaoRepository
{
  /// <summary>Uma consulta de reposicao sem nada a repor, sobre fakes soltos: para quem so precisa construir o caso de uso.</summary>
  internal static ConsultaDeReposicao ConsultaVazia(FakeExecucaoRepo? execucao = null, FakeEstruturaRepo? estruturas = null)
  {
    estruturas ??= new FakeEstruturaRepo();
    execucao ??= new FakeExecucaoRepo(estruturas);
    return new ConsultaDeReposicao(new FakeReposicaoRepo(), new FakeBaixaRepo(execucao, estruturas), execucao);
  }

  public void Adicionar(Reclamacao reclamacao) => throw new NotSupportedException();
  public Task<Reclamacao?> ObterReclamacaoAsync(int id, CancellationToken ct) => Task.FromResult<Reclamacao?>(null);

  public Task<IReadOnlyList<Reclamacao>> ListarReclamacoesAsync(int pedidoId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Reclamacao>>([]);

  public Task<decimal> SomarReclamadoAsync(int pecaId, CancellationToken ct) => Task.FromResult(0m);

  public Task<bool> MarcarReclamacaoEstornadaAsync(int id, int usuarioId, DateTime em, CancellationToken ct) =>
      Task.FromResult(false);

  public Task<bool> PedidoExisteAsync(int pedidoId, CancellationToken ct) => Task.FromResult(false);

  public Task<IReadOnlyDictionary<int, string>> ListarNumerosDePedidosAsync(IReadOnlyCollection<int> pedidoIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyDictionary<int, string>>(new Dictionary<int, string>());

  public Task<IReadOnlyList<Perda>> ListarPerdasSemRetrabalhoAsync(IReadOnlyCollection<int> pedidoIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Perda>>([]);

  public Task<IReadOnlyList<Reclamacao>> ListarReclamacoesSemRetrabalhoAsync(IReadOnlyCollection<int> pedidoIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Reclamacao>>([]);

  public void Adicionar(DispensaDeReposicao dispensa) => throw new NotSupportedException();

  public Task<DispensaDeReposicao?> ObterDispensaAsync(int id, CancellationToken ct) => Task.FromResult<DispensaDeReposicao?>(null);

  public Task<IReadOnlyList<DispensaDeReposicao>> ListarDispensasVigentesAsync(
      IReadOnlyCollection<int> perdaIds, IReadOnlyCollection<int> reclamacaoIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<DispensaDeReposicao>>([]);

  public Task<bool> MarcarDispensaEstornadaAsync(int id, int usuarioId, DateTime em, CancellationToken ct) => Task.FromResult(false);

  public Task<bool> MarcarPerdaRepostaAsync(int perdaId, int pedidoRetrabalhoId, CancellationToken ct) => Task.FromResult(false);

  public Task<bool> MarcarReclamacaoRepostaAsync(int reclamacaoId, int pedidoRetrabalhoId, CancellationToken ct) =>
      Task.FromResult(false);

  public Task<IReadOnlyList<RetrabalhoDoPedido>> ListarRetrabalhosAsync(int pedidoOrigemId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<RetrabalhoDoPedido>>([]);
}

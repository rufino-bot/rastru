using Rastreamento.Application.Tests.Estrutura;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Baixa;

/// <summary>
/// Fake em memoria de <see cref="IBaixaRepository"/>, no molde do <see cref="Expedicao.FakeExpedicaoRepo"/>: no codigo
/// real o repositorio nao tem `Salvar` proprio, e o fake se liga ao <see cref="FakeExecucaoRepo"/> por
/// <see cref="FakeExecucaoRepo.IParticipanteDaTransacao"/> (`Adicionar` so vira registro no `SalvarAlteracoesAsync`
/// dele, que tambem da o Id). A arvore e a do <see cref="FakeEstruturaRepo"/>; as linhas, as do livro do
/// <c>Execucao</c>; o status do Pedido, o arranjo dele.
/// </summary>
public class FakeBaixaRepo : IBaixaRepository, FakeExecucaoRepo.IParticipanteDaTransacao
{
  private readonly FakeExecucaoRepo _execucao;
  private readonly FakeEstruturaRepo _estruturas;
  private readonly List<Perda> _pendentes = new();
  private int _proximoId = 9000;

  public FakeBaixaRepo(FakeExecucaoRepo execucao, FakeEstruturaRepo estruturas)
  {
    _execucao = execucao;
    _estruturas = estruturas;
    execucao.Participar(this);
  }

  public List<Perda> Perdas { get; } = new();

  public void Adicionar(Perda perda) => _pendentes.Add(perda);

  public Task<Perda?> ObterAsync(int id, CancellationToken ct) => Task.FromResult(Perdas.SingleOrDefault(p => p.Id == id));

  public Task<IReadOnlyList<Movimentacao>> ListarLinhasDasPerdasAsync(IReadOnlyCollection<int> perdaIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Movimentacao>>(_execucao.Movimentacoes
          .Where(m => m.PerdaId is int p && perdaIds.Contains(p)).OrderBy(m => m.Id).ToList());

  public Task<IReadOnlyList<int>> ListarSubarvoreAsync(int noId, CancellationToken ct)
  {
    var ids = new List<int>();
    var fronteira = _estruturas.Itens.Where(i => i.Id == noId).Select(i => i.Id).ToList();
    while (fronteira.Count > 0)
    {
      ids.AddRange(fronteira);
      var atual = fronteira;
      fronteira = _estruturas.Itens.Where(i => i.EstruturaPaiId is int pai && atual.Contains(pai)).Select(i => i.Id).ToList();
    }
    return Task.FromResult<IReadOnlyList<int>>(ids.Order().ToList());
  }

  public Task<IReadOnlyList<int>> ListarCadeiaAteOTopoAsync(int noId, CancellationToken ct)
  {
    var cadeia = new List<int>();
    var pai = _estruturas.Itens.SingleOrDefault(i => i.Id == noId)?.EstruturaPaiId;
    while (pai is int id)
    {
      cadeia.Add(id);
      pai = _estruturas.Itens.Single(i => i.Id == id).EstruturaPaiId;
    }
    return Task.FromResult<IReadOnlyList<int>>(cadeia);
  }

  public Task<IReadOnlyDictionary<int, string>> ListarStatusDosPedidosAsync(
      IReadOnlyCollection<int> pedidoIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyDictionary<int, string>>(_execucao.StatusDoPedido
          .Where(kv => pedidoIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));

  public Task<IReadOnlyList<int>> ListarPecasDoEscopoAsync(int pedidoId, int? agrupamentoId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<int>>(_estruturas.Itens
          .Where(i => i.EstruturaPaiId is null && _execucao.Agrupamentos.TryGetValue(i.AgrupamentoId, out var ag)
              && ag.PedidoId == pedidoId && (agrupamentoId is null || i.AgrupamentoId == agrupamentoId))
          .Select(i => i.Id).Order().ToList());

  public Task<int?> ObterPedidoDoAgrupamentoAsync(int agrupamentoId, CancellationToken ct) =>
      Task.FromResult(_execucao.Agrupamentos.TryGetValue(agrupamentoId, out var ag) ? (int?)ag.PedidoId : null);

  public Task<IReadOnlyList<Perda>> ListarPerdasDosNosAsync(IReadOnlyCollection<int> noIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Perda>>(Perdas
          .Where(p => _execucao.Movimentacoes.Any(m => m.PerdaId == p.Id && m.Tipo == TiposDeMovimentacao.Perda
                                                       && noIds.Contains(m.EstruturaItemId)))
          .OrderByDescending(p => p.DataHora).ThenByDescending(p => p.Id).ToList());

  public Task<IReadOnlyList<Perda>> ListarPerdasDoPedidoAsync(int pedidoId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Perda>>(Perdas.Where(p => p.PedidoId == pedidoId)
          .OrderByDescending(p => p.DataHora).ThenByDescending(p => p.Id).ToList());

  public Task<IReadOnlyList<Perda>> ListarPerdasLigadasAsync(int perdaDeOrigemId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Perda>>(Perdas.Where(p => p.PerdaDeOrigemId == perdaDeOrigemId).OrderBy(p => p.Id).ToList());

  void FakeExecucaoRepo.IParticipanteDaTransacao.AoSalvar()
  {
    foreach (var perda in _pendentes)
    {
      perda.Id = _proximoId++;
      Perdas.Add(perda);
    }
    _pendentes.Clear();
  }

  Action FakeExecucaoRepo.IParticipanteDaTransacao.TirarRetrato()
  {
    var perdas = Perdas.ToList();
    return () =>
    {
      Perdas.Clear();
      Perdas.AddRange(perdas);
    };
  }

  void FakeExecucaoRepo.IParticipanteDaTransacao.DescartarPendentes() => _pendentes.Clear();
}

using Rastreamento.Application.Execucao;
using Rastreamento.Application.Tests.Estrutura;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Expedicao;

/// <summary>
/// Fake em memoria de <see cref="IExpedicaoRepository"/>, no molde do <see cref="Separacao.FakeSeparacaoRepo"/>.
///
/// <para>
/// No codigo real o repositorio nao tem `Salvar` proprio: o `DbContext` e o mesmo do
/// <see cref="IExecucaoRepository"/>. O fake reproduz isso ligando-se ao <see cref="FakeExecucaoRepo"/> por
/// <see cref="FakeExecucaoRepo.IParticipanteDaTransacao"/>: `Adicionar` so vira registro no
/// `SalvarAlteracoesAsync` do <c>Execucao</c> (que atribui os Ids), o que ficou pendente ao fim da transacao
/// e descartado, e uma transacao desfeita devolve os registros ao que eram.
/// </para>
///
/// <para>
/// As linhas do livro que cada registro aponta sao as de <c>Execucao.Movimentacoes</c> — o fake nao tem
/// livro proprio. `Estruturas` e `Execucao` dao o Pedido de cada Peca para
/// <see cref="ListarPedidosDeLiberacaoAsync"/> e <see cref="ListarPedidosDasPecasAsync"/>.
/// </para>
/// </summary>
public class FakeExpedicaoRepo : IExpedicaoRepository, FakeExecucaoRepo.IParticipanteDaTransacao
{
  private readonly FakeExecucaoRepo _execucao;
  private readonly FakeEstruturaRepo _estruturas;
  private readonly List<object> _pendentes = new();
  private int _proximoId = 8000;

  public FakeExpedicaoRepo(FakeExecucaoRepo execucao, FakeEstruturaRepo estruturas)
  {
    _execucao = execucao;
    _estruturas = estruturas;
    execucao.Participar(this);
  }

  public List<RelatorioDimensional> Afericoes { get; } = new();
  public List<ContagemDimensional> Contagens { get; } = new();
  public List<LiberacaoDimensional> Liberacoes { get; } = new();
  public List<Remessa> Remessas { get; } = new();

  public void Adicionar(RelatorioDimensional afericao) => _pendentes.Add(afericao);

  public void Adicionar(ContagemDimensional contagem) => _pendentes.Add(contagem);

  public void Adicionar(LiberacaoDimensional liberacao) => _pendentes.Add(liberacao);

  public void Adicionar(Remessa remessa) => _pendentes.Add(remessa);

  public Task<IReadOnlyList<RelatorioDimensional>> ListarAfericoesAsync(IReadOnlyCollection<int> pecaIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<RelatorioDimensional>>(
          Afericoes.Where(a => pecaIds.Contains(a.EstruturaItemId)).OrderBy(a => a.Id).ToList());

  public Task<RelatorioDimensional?> ObterAfericaoAsync(int id, CancellationToken ct) =>
      Task.FromResult(Afericoes.SingleOrDefault(a => a.Id == id));

  public Task<IReadOnlyList<ContagemDimensional>> ListarContagensAsync(IReadOnlyCollection<int> pecaIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<ContagemDimensional>>(
          Contagens.Where(c => pecaIds.Contains(c.EstruturaItemId)).OrderBy(c => c.Id).ToList());

  public Task<ContagemDimensional?> ObterContagemAsync(int id, CancellationToken ct) =>
      Task.FromResult(Contagens.SingleOrDefault(c => c.Id == id));

  public Task<IReadOnlyList<LiberacaoDimensional>> ListarLiberacoesAsync(IReadOnlyCollection<int> pecaIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<LiberacaoDimensional>>(
          Liberacoes.Where(l => pecaIds.Contains(l.EstruturaItemId)).OrderBy(l => l.Id).ToList());

  public Task<IReadOnlyList<LiberacaoDimensional>> ListarPedidosDeLiberacaoAsync(CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<LiberacaoDimensional>>(Liberacoes
          .Where(l => l.Tipo == TiposDeLiberacao.Pedido && PedidoEstaEncerrado(l.EstruturaItemId) == false)
          .OrderBy(l => l.Id).ToList());

  /// <summary>Nulo quando o no ou o Agrupamento dele nao esta no arranjo do cenario (o real nao devolveria a linha).</summary>
  private bool? PedidoEstaEncerrado(int estruturaItemId)
  {
    var no = _estruturas.Itens.SingleOrDefault(i => i.Id == estruturaItemId);
    if (no is null || !_execucao.Agrupamentos.TryGetValue(no.AgrupamentoId, out var ag)) return null;
    return _execucao.StatusDoPedido.GetValueOrDefault(ag.PedidoId) is "Concluido" or "Cancelado";
  }

  public Task<LiberacaoDimensional?> ObterLiberacaoAsync(int id, CancellationToken ct) =>
      Task.FromResult(Liberacoes.SingleOrDefault(l => l.Id == id));

  public Task<Remessa?> ObterRemessaAsync(int id, CancellationToken ct) =>
      Task.FromResult(Remessas.SingleOrDefault(r => r.Id == id));

  public Task<IReadOnlyList<Remessa>> ListarRemessasDesdeAsync(DateTime desdeUtc, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Remessa>>(Remessas
          .Where(r => r.DataHora >= desdeUtc).OrderByDescending(r => r.DataHora).ThenByDescending(r => r.Id).ToList());

  public Task<IReadOnlyList<Movimentacao>> ListarLinhasDasContagensAsync(IReadOnlyCollection<int> contagemIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Movimentacao>>(_execucao.Movimentacoes
          .Where(m => m.ContagemId is int c && contagemIds.Contains(c)).OrderBy(m => m.Id).ToList());

  public Task<IReadOnlyList<Movimentacao>> ListarLinhasDasLiberacoesAsync(IReadOnlyCollection<int> liberacaoIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Movimentacao>>(_execucao.Movimentacoes
          .Where(m => m.LiberacaoId is int l && liberacaoIds.Contains(l)).OrderBy(m => m.Id).ToList());

  public Task<IReadOnlyList<Movimentacao>> ListarLinhasDasRemessasAsync(IReadOnlyCollection<int> remessaIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Movimentacao>>(_execucao.Movimentacoes
          .Where(m => m.RemessaId is int r && remessaIds.Contains(r)).OrderBy(m => m.Id).ToList());

  public Task<IReadOnlyDictionary<int, PedidoDaPeca>> ListarPedidosDasPecasAsync(
      IReadOnlyCollection<int> pecaIds, CancellationToken ct)
  {
    var pedidos = new Dictionary<int, PedidoDaPeca>();
    foreach (var no in _estruturas.Itens.Where(i => pecaIds.Contains(i.Id)))
      if (_execucao.Agrupamentos.TryGetValue(no.AgrupamentoId, out var ag))
        pedidos[no.Id] = new PedidoDaPeca(ag.PedidoId, ag.PedidoNumero, _execucao.StatusDoPedido[ag.PedidoId]);
    return Task.FromResult<IReadOnlyDictionary<int, PedidoDaPeca>>(pedidos);
  }

  public Task<IReadOnlySet<int>> ListarAfericoesEstornadasAsync(IReadOnlyCollection<int> afericaoIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlySet<int>>(Afericoes
          .Where(a => a.EstornoDeId is int original && afericaoIds.Contains(original))
          .Select(a => a.EstornoDeId!.Value).ToHashSet());

  void FakeExecucaoRepo.IParticipanteDaTransacao.AoSalvar()
  {
    foreach (var pendente in _pendentes)
      switch (pendente)
      {
        case RelatorioDimensional a: a.Id = _proximoId++; Afericoes.Add(a); break;
        case ContagemDimensional c: c.Id = _proximoId++; Contagens.Add(c); break;
        case LiberacaoDimensional l: l.Id = _proximoId++; Liberacoes.Add(l); break;
        case Remessa r: r.Id = _proximoId++; Remessas.Add(r); break;
      }
    _pendentes.Clear();
  }

  Action FakeExecucaoRepo.IParticipanteDaTransacao.TirarRetrato()
  {
    var afericoes = Afericoes.ToList();
    var contagens = Contagens.ToList();
    var liberacoes = Liberacoes.ToList();
    var remessas = Remessas.ToList();
    return () =>
    {
      Afericoes.Clear(); Afericoes.AddRange(afericoes);
      Contagens.Clear(); Contagens.AddRange(contagens);
      Liberacoes.Clear(); Liberacoes.AddRange(liberacoes);
      Remessas.Clear(); Remessas.AddRange(remessas);
    };
  }

  void FakeExecucaoRepo.IParticipanteDaTransacao.DescartarPendentes() => _pendentes.Clear();
}

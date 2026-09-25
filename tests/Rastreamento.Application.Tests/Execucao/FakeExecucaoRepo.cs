using Rastreamento.Application.Execucao;
using Rastreamento.Application.Tests.Estrutura;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// Fake em memoria de <see cref="IExecucaoRepository"/>. Compartilha `Itens` e `Roteiros` com o
/// <see cref="FakeEstruturaRepo"/> que recebe, porque o caso de uso le a arvore por um e o livro pelo
/// outro. Soma saldo com `Livro.SomarSaldos` — a mesma conta que o teste de banco
/// `Saldos_do_banco_batem_com_a_soma_em_CSharp` prova igual ao SQL. `Adicionar` so vira linha no
/// `SalvarAlteracoesAsync`, como no EF; o que ficou pendente ao fim da transacao e descartado.
/// </summary>
public class FakeExecucaoRepo : IExecucaoRepository
{
  private readonly FakeEstruturaRepo _estruturas;
  private readonly List<Movimentacao> _movimentosPendentes = new();
  private readonly List<Montagem> _montagensPendentes = new();
  private int _proximoId = 5000;
  private bool _emTransacao;

  public FakeExecucaoRepo(FakeEstruturaRepo estruturas) => _estruturas = estruturas;

  public List<Movimentacao> Movimentacoes { get; } = new();
  public List<Montagem> Montagens { get; } = new();

  /// <summary>AgrupamentoId -> (Codigo, PedidoId, PedidoNumero). Arranjo do teste.</summary>
  public Dictionary<int, (string Codigo, int PedidoId, string PedidoNumero)> Agrupamentos { get; } = new();

  public Dictionary<int, string> StatusDoPedido { get; } = new();
  public Dictionary<int, string> Usuarios { get; } = new();

  /// <summary>Os Ids de cada `TravarNosAsync`, como chegaram — prova de QUEM o caso de uso trava.</summary>
  public List<IReadOnlyList<int>> Travas { get; } = new();

  public int Transacoes { get; private set; }
  public int Saves { get; private set; }

  /// <summary>A proxima transacao sobe o que o repositorio real sobe num deadlock.</summary>
  public bool ConflitoNaProximaTransacao { get; set; }

  public async Task<T> EmTransacaoAsync<T>(Func<Task<T>> trabalho, CancellationToken ct)
  {
    if (ConflitoNaProximaTransacao)
    {
      ConflitoNaProximaTransacao = false;
      throw new ConflitoDeConcorrenciaException(new InvalidOperationException("deadlock simulado"));
    }

    Transacoes++;
    _emTransacao = true;
    try
    {
      return await trabalho();
    }
    finally
    {
      _emTransacao = false;
      _movimentosPendentes.Clear();
      _montagensPendentes.Clear();
    }
  }

  public Task<IReadOnlyList<EstruturaItem>> TravarNosAsync(IEnumerable<int> ids, CancellationToken ct)
  {
    if (!_emTransacao) throw new InvalidOperationException("TravarNosAsync fora de EmTransacaoAsync.");
    var pedidos = ids.ToList();
    Travas.Add(pedidos);
    return Task.FromResult<IReadOnlyList<EstruturaItem>>(
        _estruturas.Itens.Where(i => pedidos.Contains(i.Id)).OrderBy(i => i.Id).ToList());
  }

  public Task<IReadOnlyList<EstruturaItem>> ListarNosAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<EstruturaItem>>(
          _estruturas.Itens.Where(i => ids.Contains(i.Id)).OrderBy(i => i.Id).ToList());

  public Task<IReadOnlyList<EstruturaItem>> ListarFilhosAsync(int paiId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<EstruturaItem>>(
          _estruturas.Itens.Where(i => i.EstruturaPaiId == paiId).OrderBy(i => i.Id).ToList());

  public Task<IReadOnlyList<int>> ListarIdsDaSubarvoreAsync(int id, CancellationToken ct)
  {
    var ids = new List<int>();
    var fronteira = _estruturas.Itens.Where(i => i.Id == id).Select(i => i.Id).ToList();
    while (fronteira.Count > 0)
    {
      ids.AddRange(fronteira);
      var atual = fronteira;
      fronteira = _estruturas.Itens
          .Where(i => i.EstruturaPaiId is int pai && atual.Contains(pai)).Select(i => i.Id).ToList();
    }
    return Task.FromResult<IReadOnlyList<int>>(ids);
  }

  public Task<IReadOnlyList<ContextoDoNo>> ListarNosEmProducaoAsync(CancellationToken ct)
  {
    var lista = new List<ContextoDoNo>();
    foreach (var item in _estruturas.Itens.OrderBy(i => i.Id))
    {
      if (!Agrupamentos.TryGetValue(item.AgrupamentoId, out var ag)) continue;
      var status = StatusDoPedido[ag.PedidoId];
      if (status is "Concluido" or "Cancelado") continue;
      lista.Add(new ContextoDoNo(item, ag.PedidoId, ag.PedidoNumero, item.AgrupamentoId, ag.Codigo));
    }
    return Task.FromResult<IReadOnlyList<ContextoDoNo>>(lista);
  }

  public Task<PedidoDoNo?> ObterPedidoDoNoAsync(int estruturaItemId, CancellationToken ct)
  {
    var item = _estruturas.Itens.SingleOrDefault(i => i.Id == estruturaItemId);
    if (item is null || !Agrupamentos.TryGetValue(item.AgrupamentoId, out var ag))
      return Task.FromResult<PedidoDoNo?>(null);
    return Task.FromResult<PedidoDoNo?>(new PedidoDoNo(ag.PedidoId, StatusDoPedido[ag.PedidoId]));
  }

  public Task MarcarPedidoEmProducaoAsync(int pedidoId, CancellationToken ct)
  {
    if (StatusDoPedido.GetValueOrDefault(pedidoId) == "Aberto") StatusDoPedido[pedidoId] = "EmProducao";
    return Task.CompletedTask;
  }

  public Task<IReadOnlyList<SaldoLiquido>> ListarSaldosAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
      Task.FromResult(Livro.SomarSaldos(Movimentacoes.Where(m => ids.Contains(m.EstruturaItemId))));

  public Task<IReadOnlyDictionary<int, decimal>> ListarTotaisMontadosAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct) =>
      Task.FromResult<IReadOnlyDictionary<int, decimal>>(Montagens
          .Where(g => ids.Contains(g.EstruturaItemId) && g.EstornadaEm is null)
          .GroupBy(g => g.EstruturaItemId)
          .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantidade)));

  public Task<IReadOnlyList<(int EstruturaItemId, int Ordem)>> ListarPassosAlcancadosAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct)
  {
    var passos = new SortedSet<(int EstruturaItemId, int Ordem)>();
    foreach (var m in Movimentacoes.Where(m => ids.Contains(m.EstruturaItemId)))
    {
      if (m.OrigemOrdem is int origem) passos.Add((m.EstruturaItemId, origem));
      if (m.DestinoOrdem is int destino) passos.Add((m.EstruturaItemId, destino));
    }
    return Task.FromResult<IReadOnlyList<(int EstruturaItemId, int Ordem)>>(passos.ToList());
  }

  public Task<Movimentacao?> ObterMovimentacaoAsync(int id, CancellationToken ct) =>
      Task.FromResult(Movimentacoes.SingleOrDefault(m => m.Id == id));

  public Task<IReadOnlyList<Movimentacao>> ListarMovimentacoesDoNoAsync(int estruturaItemId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Movimentacao>>(
          Movimentacoes.Where(m => m.EstruturaItemId == estruturaItemId).OrderBy(m => m.Id).ToList());

  public Task<IReadOnlySet<int>> ListarEstornadasAsync(IReadOnlyCollection<int> movimentacaoIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlySet<int>>(Movimentacoes
          .Where(m => m.EstornoDeId is int original && movimentacaoIds.Contains(original))
          .Select(m => m.EstornoDeId!.Value)
          .ToHashSet());

  public Task<Montagem?> ObterMontagemAsync(int id, CancellationToken ct) =>
      Task.FromResult(Montagens.SingleOrDefault(g => g.Id == id));

  public Task<IReadOnlyList<Montagem>> ListarMontagensDoNoAsync(int paiId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Montagem>>(
          Montagens.Where(g => g.EstruturaItemId == paiId).OrderBy(g => g.Id).ToList());

  public Task<IReadOnlyList<Movimentacao>> ListarBaixasAsync(IReadOnlyCollection<int> montagemIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Movimentacao>>(Movimentacoes
          .Where(m => m.Tipo == TiposDeMovimentacao.Montagem && m.MontagemId is int g && montagemIds.Contains(g))
          .OrderBy(m => m.Id)
          .ToList());

  public Task MarcarMontagemEstornadaAsync(int montagemId, int usuarioId, DateTime em, CancellationToken ct)
  {
    var montagem = Montagens.Single(g => g.Id == montagemId);
    if (montagem.EstornadaEm is null)
    {
      montagem.EstornadaEm = em;
      montagem.EstornadaPorUsuarioId = usuarioId;
    }
    return Task.CompletedTask;
  }

  public Task<IReadOnlyDictionary<int, string>> ListarNomesDeUsuariosAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct) =>
      Task.FromResult<IReadOnlyDictionary<int, string>>(
          Usuarios.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));

  public Task SubstituirPassosNaoAlcancadosAsync(
      int estruturaItemId, int? ultimaOrdemTravada, IReadOnlyList<(int SetorId, int Ordem)> novos, CancellationToken ct)
  {
    _estruturas.Roteiros.RemoveAll(r =>
        r.EstruturaItemId == estruturaItemId && (ultimaOrdemTravada is null || r.Ordem > ultimaOrdemTravada));
    foreach (var (setorId, ordem) in novos)
      _estruturas.Roteiros.Add(new EstruturaRoteiro
      {
        Id = _proximoId++, EstruturaItemId = estruturaItemId, SetorId = setorId, Ordem = ordem,
      });
    return Task.CompletedTask;
  }

  public void Adicionar(Movimentacao movimentacao) => _movimentosPendentes.Add(movimentacao);

  public void Adicionar(Montagem montagem) => _montagensPendentes.Add(montagem);

  public Task SalvarAlteracoesAsync(CancellationToken ct)
  {
    Saves++;
    foreach (var montagem in _montagensPendentes)
    {
      montagem.Id = _proximoId++;
      Montagens.Add(montagem);
    }
    foreach (var movimento in _movimentosPendentes)
    {
      movimento.Id = _proximoId++;
      Movimentacoes.Add(movimento);
    }
    _montagensPendentes.Clear();
    _movimentosPendentes.Clear();
    return Task.CompletedTask;
  }

  /// <summary>Arranjo de teste: grava direto no livro, sem caso de uso.</summary>
  public Movimentacao Semear(Movimentacao movimentacao)
  {
    movimentacao.Id = _proximoId++;
    Movimentacoes.Add(movimentacao);
    return movimentacao;
  }
}

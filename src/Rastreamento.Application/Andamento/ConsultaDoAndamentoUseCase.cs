using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Retrabalho;
using Rastreamento.Application.Separacao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Andamento;

/// <summary>
/// O andamento de um Pedido (C10, C12 e secao 5.3 da spec da Fase 6A): por Peca ainda em aberto, a trilha do Roteiro
/// (os passos passados, o atual e os futuros, o "a iniciar", a montagem, o conserto e o local de expedicao), as
/// complicacoes e os Itens, recursivamente. Le em lote, como o Panorama: o livro, as criacoes dos Agrupamentos, as
/// pausas e o material saem de uma consulta cada para todos os nos do Pedido; a idade vem de <see cref="CalculoDaIdade"/>
/// e o que conta como parado (C5) e o de <see cref="PartesQueContam"/>. Sem cache: e a leitura de um Pedido so.
/// </summary>
public sealed class ConsultaDoAndamentoUseCase
{
  private const string ErroDePedidoNaoEncontrado = "Pedido nao encontrado.";

  private readonly IPedidoRepository _pedidos;
  private readonly IExecucaoRepository _execucao;
  private readonly IReceitaPadraoRepository _catalogo;
  private readonly LeitorDeComplicacoes _complicacoes;
  private readonly TimeProvider _relogio;
  private readonly LeitorDeEstado _leitor;

  public ConsultaDoAndamentoUseCase(
      IPedidoRepository pedidos, IExecucaoRepository execucao, IEstruturaRepository estruturas,
      IReceitaPadraoRepository catalogo, ISeparacaoRepository separacao, ConsultaDeReposicao reposicao, TimeProvider relogio)
  {
    _pedidos = pedidos;
    _execucao = execucao;
    _catalogo = catalogo;
    _complicacoes = new LeitorDeComplicacoes(separacao, reposicao);
    _relogio = relogio;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
  }

  public Task<Result<AndamentoDoPedidoDto>> Obter(int pedidoId, CancellationToken ct) =>
      _execucao.ConsultarAsync(() => ObterAsync(pedidoId, ct), ct);

  private async Task<Result<AndamentoDoPedidoDto>> ObterAsync(int pedidoId, CancellationToken ct)
  {
    var pedido = await _pedidos.ObterPorIdAsync(pedidoId, ct);
    if (pedido is null)
      return Result<AndamentoDoPedidoDto>.Falha(ErroDePedidoNaoEncontrado, TipoDeErro.NaoEncontrado);

    var agora = _relogio.GetUtcNow().UtcDateTime;
    var todos = await _execucao.ListarNosDoPedidoAsync(pedidoId, ct);
    var linhas = todos.Count == 0 ? [] : await _execucao.ListarMovimentacoesAsync(todos.Select(x => x.No.Id).ToList(), ct);

    var inicio = TempoDoPedido.InicioReal(linhas);
    var pausado = inicio is null
        ? 0L
        : (long)TempoDoPedido.PausadoDepoisDe(inicio.Value, await _execucao.ListarPausasDoPedidoAsync(pedidoId, ct), agora).TotalSeconds;

    if (todos.Count == 0 || pedido.Status is "Concluido" or "Cancelado")
      return Result<AndamentoDoPedidoDto>.Ok(new AndamentoDoPedidoDto(agora, pedido.DataAbertura, inicio, pausado, null, []));

    var estado = await _leitor.CarregarAsync(todos.Select(x => x.No).ToList(), ct);
    var criacoes = await _execucao.ListarCriacaoDosAgrupamentosAsync(todos.Select(x => x.AgrupamentoId).Distinct().ToList(), ct);
    var idades = CalculoDaIdade.Calcular(
        todos.Select(x => new NoDaIdade(x.No.Id, x.No.Quantidade, criacoes.GetValueOrDefault(x.AgrupamentoId))).ToList(), linhas);

    var todosOsIds = todos.Select(x => x.No.Id).ToList();
    var dados = await _complicacoes.LerAsync(todosOsIds, [pedidoId], ct);
    var setores = (await _catalogo.ObterSetoresPorIdAsync(SetoresCitados(estado.Calc), ct)).ToDictionary(s => s.Id, s => s.Nome);

    var filhosDe = todos.Where(x => x.No.EstruturaPaiId is not null)
        .GroupBy(x => x.No.EstruturaPaiId!.Value)
        .ToDictionary(g => g.Key, g => g.Select(x => x.No.Id).Order().ToList());
    var montador = new Montador(estado, idades, dados, setores, filhosDe);

    var pecas = new List<AndamentoDoNoDto>();
    foreach (var raiz in todos.Where(x => x.No.EstruturaPaiId is null).OrderBy(x => x.No.Id))
      if (montador.Montar(raiz.No.Id, ehPeca: true, out _) is AndamentoDoNoDto peca) pecas.Add(peca);

    // O mais parado: a menor chegada entre os blocos que contam, o menor Id do no desempatando.
    var maisParado = montador.Candidatos.Count == 0
        ? null
        : montador.Candidatos.OrderBy(c => c.Desde).ThenBy(c => c.NoId).Select(c => new MaisParadoDto(c.NoId, c.Chave)).First();
    return Result<AndamentoDoPedidoDto>.Ok(new AndamentoDoPedidoDto(agora, pedido.DataAbertura, inicio, pausado, maisParado, pecas));
  }

  /// <summary>Os Setores que a trilha cita: os do Roteiro de cada no e os de montagem e de conserto onde ha saldo.</summary>
  private static List<int> SetoresCitados(CalculadoraDeExecucao calc)
  {
    var ids = new HashSet<int>();
    foreach (var no in calc.Nos)
    {
      foreach (var passo in no.Roteiro) ids.Add(passo.SetorId);
      foreach (var s in calc.Saldos(no.Id))
        if (s.Local.SetorId is int setor) ids.Add(setor);
    }
    return ids.ToList();
  }

  /// <summary>
  /// Monta a trilha de um no e, recursivamente, a dos Itens dele. `Candidatos` acumula, dos nos que entraram, as datas
  /// de chegada de cada bloco que conta, para a escolha do mais parado.
  /// </summary>
  private sealed class Montador(
      EstadoDeExecucao estado, IdadesDoLivro idades, DadosDeComplicacao dados,
      IReadOnlyDictionary<int, string> setores, IReadOnlyDictionary<int, List<int>> filhosDe)
  {
    private readonly CalculadoraDeExecucao _calc = estado.Calc;

    public List<(DateTime Desde, int NoId, string Chave)> Candidatos { get; } = [];

    /// <summary>
    /// Nulo quando o no nao entra. `conta` diz se algo do no ou dos descendentes conta (C5, algum bloco com quantidade
    /// agora): a Peca so entra quando conta; o Item tambem quando algum passo ja passou, ou algum filho dele entra.
    /// </summary>
    public AndamentoDoNoDto? Montar(int id, bool ehPeca, out bool conta)
    {
      var itens = new List<AndamentoDoNoDto>();
      var algumFilhoConta = false;
      foreach (var filho in filhosDe.GetValueOrDefault(id) ?? [])
        if (Montar(filho, ehPeca: false, out var contaOFilho) is AndamentoDoNoDto item)
        {
          itens.Add(item);
          algumFilhoConta |= contaOFilho;
        }

      var trilha = Trilha(id);
      conta = algumFilhoConta || trilha.Any(b => b.Situacao == SituacoesDoBloco.Atual);
      var entra = ehPeca
          ? conta
          : conta || itens.Count > 0 || trilha.Any(b => b.Situacao == SituacoesDoBloco.Passado);
      if (!entra) return null;

      foreach (var b in trilha)
      {
        if (b.Desde is DateTime d) Candidatos.Add((d, id, b.Chave));
        if (b.AguardandoColetaDesde is DateTime c) Candidatos.Add((c, id, b.Chave));
      }

      var subarvore = Subarvore(id);
      var complicacoes = ComplicacoesDaPeca.Calcular(
          subarvore, _calc, dados.Resumos.Where(r => subarvore.Contains(r.EstruturaItemId)).ToList(), dados.Descricoes,
          ehPeca ? dados.ARepor.GetValueOrDefault(id) : 0m, estado.Nome);

      return new AndamentoDoNoDto(
          id, estado.Codigos.GetValueOrDefault(id) ?? string.Empty, estado.Nome(id), _calc.No(id).Quantidade, trilha, complicacoes, itens);
    }

    private HashSet<int> Subarvore(int id)
    {
      var nos = new HashSet<int>();
      var pilha = new Stack<int>([id]);
      while (pilha.Count > 0)
      {
        var atual = pilha.Pop();
        if (!nos.Add(atual)) continue;
        foreach (var filho in filhosDe.GetValueOrDefault(atual) ?? []) pilha.Push(filho);
      }
      return nos;
    }

    /// <summary>
    /// Os blocos do no, na ordem de C10: o "a iniciar" (so quando conta, C5), os passos do Roteiro, a montagem por Setor,
    /// o conserto por Setor e o local de expedicao. Bloco sem nada nao entra, exceto o passo futuro; no sem Roteiro nao
    /// tem passos, e a trilha e so o que existe.
    /// </summary>
    private List<BlocoDaTrilhaDto> Trilha(int id)
    {
      var blocos = new List<BlocoDaTrilhaDto>();
      var saldos = _calc.Saldos(id);
      string? Nome(int? setorId) => setorId is int s ? setores.GetValueOrDefault(s, string.Empty) : null;
      decimal Soma(Func<Local, bool> onde) => saldos.Where(s => onde(s.Local) && s.Quantidade > 0m).Sum(s => s.Quantidade);
      DateTime? MaisAntiga(Func<Local, bool> onde) => PartesQueContam.MaisAntiga(_calc, idades, id, onde);

      var aIniciar = Soma(l => l.Posicao == Posicoes.AIniciar);
      if (aIniciar > 0m && PartesQueContam.Conta(_calc, id, Local.AIniciar))
        blocos.Add(new BlocoDaTrilhaDto(
            TiposDeBloco.AIniciar, "AIniciar", null, null, null, SituacoesDoBloco.Atual, aIniciar,
            MaisAntiga(l => l.Posicao == Posicoes.AIniciar), 0m, null, null));

      foreach (var passo in _calc.No(id).Roteiro.OrderBy(p => p.Ordem))
      {
        var (setorId, ordem) = (passo.SetorId, passo.Ordem);
        var noSetor = Soma(l => l.Posicao == Posicoes.NoSetor && l.SetorId == setorId && l.Ordem == ordem);
        var coleta = Soma(l => l.Posicao == Posicoes.AguardandoColeta && l.SetorId == setorId && l.Ordem == ordem);
        var quantidade = noSetor + coleta;
        var permanencia = idades.PermanenciaMedia(id, ordem);
        var situacao = quantidade > 0m ? SituacoesDoBloco.Atual
            : permanencia is not null ? SituacoesDoBloco.Passado
            : SituacoesDoBloco.Futuro;
        blocos.Add(new BlocoDaTrilhaDto(
            TiposDeBloco.Passo, $"Passo:{ordem}", ordem, setorId, Nome(setorId), situacao, quantidade,
            MaisAntiga(l => l.Posicao == Posicoes.NoSetor && l.SetorId == setorId && l.Ordem == ordem),
            coleta,
            MaisAntiga(l => l.Posicao == Posicoes.AguardandoColeta && l.SetorId == setorId && l.Ordem == ordem),
            permanencia is TimeSpan p ? (long)p.TotalSeconds : null));
      }

      foreach (var setorId in saldos.Where(s => s.Local.Posicao == Posicoes.AguardandoMontagem && s.Quantidade > 0m)
                   .Select(s => s.Local.SetorId!.Value).Distinct().Order())
        blocos.Add(new BlocoDaTrilhaDto(
            TiposDeBloco.Montagem, $"Montagem:{setorId}", null, setorId, Nome(setorId), SituacoesDoBloco.Atual,
            Soma(l => l.Posicao == Posicoes.AguardandoMontagem && l.SetorId == setorId), 
            MaisAntiga(l => l.Posicao == Posicoes.AguardandoMontagem && l.SetorId == setorId), 0m, null, null));

      foreach (var setorId in saldos.Where(s => Posicoes.DoConserto.Contains(s.Local.Posicao) && s.Quantidade > 0m)
                   .Select(s => s.Local.SetorId!.Value).Distinct().Order())
        blocos.Add(new BlocoDaTrilhaDto(
            TiposDeBloco.Conserto, $"Conserto:{setorId}", null, setorId, Nome(setorId), SituacoesDoBloco.Atual,
            Soma(l => Posicoes.DoConserto.Contains(l.Posicao) && l.SetorId == setorId),
            MaisAntiga(l => Posicoes.DoConserto.Contains(l.Posicao) && l.SetorId == setorId), 0m, null, null));

      var naExpedicao = Soma(l => Posicoes.NoLocalDeExpedicao.Contains(l.Posicao));
      if (naExpedicao > 0m)
        blocos.Add(new BlocoDaTrilhaDto(
            TiposDeBloco.Expedicao, "Expedicao", null, null, null, SituacoesDoBloco.Atual, naExpedicao,
            MaisAntiga(l => Posicoes.NoLocalDeExpedicao.Contains(l.Posicao)), 0m, null, null));

      return blocos;
    }
  }
}

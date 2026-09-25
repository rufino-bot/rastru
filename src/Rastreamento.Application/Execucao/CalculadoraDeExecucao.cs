using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

public readonly record struct PassoDoCalculo(int SetorId, int Ordem);

/// <summary>Um no como a calculadora o ve. `Roteiro` em ordem de `Ordem`.</summary>
public sealed record NoDoCalculo(
    int Id, int? PaiId, decimal Quantidade, decimal? QuantidadePorPai, IReadOnlyList<PassoDoCalculo> Roteiro)
{
  public bool EhPeca => PaiId is null;
}

public enum TipoDeDestino { ProximoPasso, Expedicao, Montagem }

/// <summary>
/// Para onde vai o que aguarda coleta (spec secao 7.3). `Passo` so em `ProximoPasso`; `PaiId`,
/// `SugestaoSetorId` e `SetoresPossiveis` so em `Montagem`. Pai sem Roteiro: lista vazia, sem sugestao
/// — a entrega e recusada com `PaiSemRoteiro`, e o item aparece assim mesmo (desvio D7 do plano 2).
/// </summary>
public sealed record DestinoCalculado(
    TipoDeDestino Tipo, PassoDoCalculo? Passo, int? PaiId, int? SugestaoSetorId, IReadOnlyList<int> SetoresPossiveis)
{
  public bool PaiSemRoteiro => Tipo == TipoDeDestino.Montagem && SetoresPossiveis.Count == 0;
}

/// <summary>Um filho direto na conta de "da para montar" (spec secao 7.6).</summary>
public sealed record FilhoNaMontagem(
    int FilhoId, decimal QuantidadePorPai, decimal Presente, decimal? NecessarioParaProxima, decimal? FaltaParaProxima);

public sealed record Montabilidade(
    int PaiId, int SetorId, decimal FaltaMontar, decimal DaParaMontar, IReadOnlyList<FilhoNaMontagem> Filhos);

/// <summary>Um "Item pronto" (regra 23): o que aguarda coleta no passo e ainda tem para onde ir.</summary>
public sealed record ColetaPendente(int EstruturaItemId, int SetorId, int Ordem, decimal Tarefa);

/// <summary>
/// A regra da fila e das tarefas (spec da Fase 3, secao 7), em funcoes puras. Recebe os nos, os saldos
/// liquidos do livro, os totais montados e os passos ja alcancados; nao le nada. As escritas validam
/// com as MESMAS funcoes que a leitura mostra — e isso que fecha, por construcao, a tela oferecer
/// "montar 3" e a API recusar 3 (secao 7.2).
///
/// Enxerga so os nos que recebe: o destino e a tarefa de um Item precisam do PAI na entrada. Ver o
/// "Contrato de uso" do plano 2, Task 3.
/// </summary>
public sealed class CalculadoraDeExecucao
{
  private static readonly IReadOnlyDictionary<Local, decimal> SemMovimento = new Dictionary<Local, decimal>();

  private readonly Dictionary<int, NoDoCalculo> _nos;
  private readonly ILookup<int, NoDoCalculo> _filhos;
  private readonly Dictionary<int, Dictionary<Local, decimal>> _liquido = new();
  private readonly IReadOnlyDictionary<int, decimal> _totaisMontados;
  private readonly Dictionary<int, HashSet<int>> _alcancados = new();

  public CalculadoraDeExecucao(
      IEnumerable<NoDoCalculo> nos,
      IEnumerable<SaldoLiquido> saldos,
      IReadOnlyDictionary<int, decimal> totaisMontados,
      IEnumerable<(int EstruturaItemId, int Ordem)> passosAlcancados)
  {
    _nos = nos.ToDictionary(n => n.Id);
    _filhos = _nos.Values.Where(n => n.PaiId is not null).OrderBy(n => n.Id).ToLookup(n => n.PaiId!.Value);

    foreach (var s in saldos)
    {
      if (!_liquido.TryGetValue(s.EstruturaItemId, out var doNo))
        _liquido[s.EstruturaItemId] = doNo = new Dictionary<Local, decimal>();
      var local = new Local(s.Posicao, s.SetorId, s.Ordem);
      doNo[local] = doNo.GetValueOrDefault(local) + s.Quantidade;
    }

    _totaisMontados = totaisMontados;

    foreach (var (item, ordem) in passosAlcancados)
    {
      if (!_alcancados.TryGetValue(item, out var doNo))
        _alcancados[item] = doNo = new HashSet<int>();
      doNo.Add(ordem);
    }
  }

  public IEnumerable<NoDoCalculo> Nos => _nos.Values.OrderBy(n => n.Id);

  public NoDoCalculo No(int id) => _nos[id];

  public IReadOnlyList<NoDoCalculo> Filhos(int paiId) => _filhos[paiId].ToList();

  public bool TemFilhos(int id) => _filhos[id].Any();

  private IReadOnlyDictionary<Local, decimal> LiquidoDo(int id) =>
      _liquido.TryGetValue(id, out var doNo) ? doNo : SemMovimento;

  /// <summary>AIniciar soma a quantidade do no; as demais posicoes sao so o liquido do livro.</summary>
  public decimal Saldo(int id, Local local) =>
      (local == Local.AIniciar ? _nos[id].Quantidade : 0m) + LiquidoDo(id).GetValueOrDefault(local);

  /// <summary>O que ja saiu de "a iniciar", descontado o que voltou por estorno.</summary>
  public decimal SaidoDeAIniciar(int id) => -LiquidoDo(id).GetValueOrDefault(Local.AIniciar);

  /// <summary>Toda posicao com saldo diferente de zero, na ordem de `Posicoes.Todas`, passo e Setor.</summary>
  public IReadOnlyList<(Local Local, decimal Quantidade)> Saldos(int id) =>
      LiquidoDo(id).Keys.Append(Local.AIniciar).Distinct()
          .Select(l => (Local: l, Quantidade: Saldo(id, l)))
          .Where(x => x.Quantidade != 0m)
          .OrderBy(x => Livro.OrdemDaPosicao(x.Local.Posicao))
          .ThenBy(x => x.Local.Ordem ?? 0)
          .ThenBy(x => x.Local.SetorId ?? 0)
          .ToList();

  public decimal TotalMontado(int id) => _totaisMontados.GetValueOrDefault(id);

  public decimal FaltaMontar(int paiId) => _nos[paiId].Quantidade - TotalMontado(paiId);

  public decimal AguardandoMontagem(int filhoId, int setorId) => Saldo(filhoId, Local.AguardandoMontagem(setorId));

  public decimal AguardandoMontagemTotal(int filhoId) =>
      LiquidoDo(filhoId).Where(kv => kv.Key.Posicao == Posicoes.AguardandoMontagem).Sum(kv => kv.Value);

  public IReadOnlyList<int> SetoresOndeAguardaMontagem(int filhoId) =>
      LiquidoDo(filhoId)
          .Where(kv => kv.Key.Posicao == Posicoes.AguardandoMontagem && kv.Value > 0m)
          .Select(kv => kv.Key.SetorId!.Value)
          .OrderBy(s => s)
          .ToList();

  /// <summary>
  /// Razao nula so existiria com dado incoerente, que `CK_EstruturaItem_QuantidadePorPai` nao deixa
  /// existir; conta como zero em vez de estourar.
  /// </summary>
  private static decimal Razao(NoDoCalculo no) => no.QuantidadePorPai ?? 0m;

  /// <summary>
  /// O que o pai ainda precisa receber deste filho (spec secao 7.4):
  /// max(0, (Quantidade(P) - totalMontado(P)) x QuantidadePorPai(c) - soma de AguardandoMontagem(c)).
  /// </summary>
  public decimal Precisa(int filhoId)
  {
    var filho = _nos[filhoId];
    if (filho.PaiId is not int paiId || !_nos.ContainsKey(paiId)) return 0m;
    return Math.Max(0m, FaltaMontar(paiId) * Razao(filho) - AguardandoMontagemTotal(filhoId));
  }

  public PassoDoCalculo? PrimeiroPasso(int id) => _nos[id].Roteiro.Count == 0 ? null : _nos[id].Roteiro[0];

  /// <summary>O passo depois de `ordem` — por passo, nao por Setor (regra 21: o Setor pode repetir).</summary>
  public PassoDoCalculo? ProximoPasso(int id, int ordem)
  {
    foreach (var passo in _nos[id].Roteiro)
      if (passo.Ordem > ordem) return passo;
    return null;
  }

  public IReadOnlySet<int> PassosAlcancados(int id) =>
      _alcancados.TryGetValue(id, out var doNo) ? doNo : new HashSet<int>();

  public IReadOnlyList<int> SetoresDoRoteiro(int id) =>
      _nos.TryGetValue(id, out var no) ? no.Roteiro.Select(p => p.SetorId).Distinct().ToList() : Array.Empty<int>();

  public DestinoCalculado DestinoDaColeta(int id, int ordem)
  {
    if (ProximoPasso(id, ordem) is PassoDoCalculo proximo)
      return new DestinoCalculado(TipoDeDestino.ProximoPasso, proximo, null, null, Array.Empty<int>());

    if (_nos[id].PaiId is not int paiId)
      return new DestinoCalculado(TipoDeDestino.Expedicao, null, null, null, Array.Empty<int>());

    return new DestinoCalculado(
        TipoDeDestino.Montagem, null, paiId, SugestaoDeMontagem(paiId), SetoresDoRoteiro(paiId));
  }

  /// <summary>
  /// Spec secao 7.3, item 3: o Setor onde o pai esta em trabalho (o de maior saldo; empate, o de menor
  /// Id); senao o do primeiro passo do pai ainda nao alcancado; senao nenhuma.
  /// </summary>
  public int? SugestaoDeMontagem(int paiId)
  {
    if (!_nos.TryGetValue(paiId, out var pai) || pai.Roteiro.Count == 0) return null;

    var emTrabalho = LiquidoDo(paiId)
        .Where(kv => kv.Key.Posicao == Posicoes.NoSetor && kv.Value > 0m)
        .GroupBy(kv => kv.Key.SetorId!.Value)
        .Select(g => (SetorId: g.Key, Quantidade: g.Sum(kv => kv.Value)))
        .OrderByDescending(x => x.Quantidade)
        .ThenBy(x => x.SetorId)
        .ToList();
    if (emTrabalho.Count > 0) return emTrabalho[0].SetorId;

    var alcancados = PassosAlcancados(paiId);
    foreach (var passo in pai.Roteiro)
      if (!alcancados.Contains(passo.Ordem)) return passo.SetorId;
    return null;
  }

  /// <summary>
  /// A tarefa do que aguarda coleta no passo (spec secao 7.4): inteira se ainda ha passo ou se e Peca;
  /// no ultimo passo de um Item, limitada ao que o pai ainda precisa receber.
  /// </summary>
  public decimal Tarefa(int id, int setorId, int ordem)
  {
    var saldo = Saldo(id, Local.AguardandoColeta(setorId, ordem));
    if (saldo <= 0m) return 0m;
    if (ProximoPasso(id, ordem) is not null || _nos[id].EhPeca) return saldo;
    return Math.Min(saldo, Precisa(id));
  }

  /// <summary>A parte do que aguarda coleta que nao e tarefa — so existe no ultimo passo de um Item.</summary>
  public decimal SobraDaColeta(int id, int setorId, int ordem) =>
      Math.Max(0m, Saldo(id, Local.AguardandoColeta(setorId, ordem)) - Tarefa(id, setorId, ordem));

  /// <summary>O que aguarda montagem alem do que o pai precisa (spec secao 7.5), no nivel do no.</summary>
  public decimal ExcessoEmMontagem(int filhoId)
  {
    var filho = _nos[filhoId];
    if (filho.PaiId is not int paiId || !_nos.ContainsKey(paiId)) return 0m;
    return Math.Max(0m, AguardandoMontagemTotal(filhoId) - FaltaMontar(paiId) * Razao(filho));
  }

  /// <summary>
  /// "Da para montar N; falta X de Y" no Setor (spec secao 7.6):
  /// N = min(falta montar, min por filho de floor(presente / razao)); o "falta" e o que cada filho
  /// precisa para a unidade N+1, so enquanto N+1 cabe no que falta montar.
  /// </summary>
  public Montabilidade CalcularMontabilidade(int paiId, int setorId)
  {
    var falta = Math.Max(0m, FaltaMontar(paiId));
    var filhos = Filhos(paiId);
    if (filhos.Count == 0) return new Montabilidade(paiId, setorId, falta, 0m, Array.Empty<FilhoNaMontagem>());

    var n = falta;
    foreach (var c in filhos)
    {
      var razao = Razao(c);
      var possivel = razao == 0m ? 0m : Math.Floor(AguardandoMontagem(c.Id, setorId) / razao);
      n = Math.Min(n, possivel);
    }

    var proxima = n + 1m;
    var haProxima = proxima <= falta;
    var lista = filhos.Select(c =>
    {
      var razao = Razao(c);
      var presente = AguardandoMontagem(c.Id, setorId);
      decimal? necessario = haProxima ? proxima * razao : null;
      decimal? faltaParaProxima = haProxima ? Math.Max(0m, proxima * razao - presente) : null;
      return new FilhoNaMontagem(c.Id, razao, presente, necessario, faltaParaProxima);
    }).ToList();

    return new Montabilidade(paiId, setorId, falta, n, lista);
  }

  /// <summary>Todo (no, passo) que aguarda coleta com tarefa positiva, por Setor, no e passo.</summary>
  public IReadOnlyList<ColetaPendente> ColetasPendentes()
  {
    var lista = new List<ColetaPendente>();
    foreach (var (id, locais) in _liquido)
    {
      if (!_nos.ContainsKey(id)) continue;
      foreach (var local in locais.Keys)
      {
        if (local.Posicao != Posicoes.AguardandoColeta) continue;
        var tarefa = Tarefa(id, local.SetorId!.Value, local.Ordem!.Value);
        if (tarefa > 0m) lista.Add(new ColetaPendente(id, local.SetorId.Value, local.Ordem.Value, tarefa));
      }
    }

    return lista.OrderBy(c => c.SetorId).ThenBy(c => c.EstruturaItemId).ThenBy(c => c.Ordem).ToList();
  }
}

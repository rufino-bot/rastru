using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

public readonly record struct PassoDoCalculo(int SetorId, int Ordem);

/// <summary>
/// Um no como a calculadora o ve. `Roteiro` em ordem de `Ordem`. `DeKit`: o no vive num Agrupamento Kit
/// (regra 25); o padrao falso mantem Avulso toda construcao que nao o diz.
/// </summary>
public sealed record NoDoCalculo(
    int Id, int? PaiId, decimal Quantidade, decimal? QuantidadePorPai, IReadOnlyList<PassoDoCalculo> Roteiro,
    bool DeKit = false)
{
  public bool EhPeca => PaiId is null;
}

public enum TipoDeDestino { ProximoPasso, Expedicao, Montagem }

/// <summary>
/// Para onde vai o que aguarda coleta (spec da Fase 3, secao 7.3, com a emenda da Fase 3D, secao 2.2).
/// `Passo`: em `ProximoPasso`, o passo seguinte; em `Montagem`, o PRIMEIRO passo do pai — onde o pai
/// comeca consumindo os filhos, sem escolha. `PaiId` so em `Montagem`. Pai sem Roteiro: `Passo` nulo —
/// a entrega e recusada com `PaiSemRoteiro`, e o item aparece assim mesmo (desvio D7 do plano 2 da Fase 3).
/// </summary>
public sealed record DestinoCalculado(TipoDeDestino Tipo, PassoDoCalculo? Passo, int? PaiId)
{
  public bool PaiSemRoteiro => Tipo == TipoDeDestino.Montagem && Passo is null;
}

/// <summary>Um filho direto na conta de "da para montar" (spec secao 7.6).</summary>
public sealed record FilhoNaMontagem(
    int FilhoId, decimal QuantidadePorPai, decimal Presente, decimal? NecessarioParaProxima, decimal? FaltaParaProxima);

public sealed record Montabilidade(
    int PaiId, int SetorId, decimal FaltaMontar, decimal DaParaMontar, IReadOnlyList<FilhoNaMontagem> Filhos);

/// <summary>Um "Item pronto" (regra 23): o que aguarda coleta no passo e ainda tem para onde ir.</summary>
public sealed record ColetaPendente(int EstruturaItemId, int SetorId, int Ordem, decimal Tarefa);

/// <summary>
/// Uma tarefa do Movimentador no conserto (D4 da spec da Fase 5A): `Local` e `AguardandoConserto` (levar ao Setor) ou
/// `ConsertoPronto` (trazer ao local de expedicao), sempre com Setor e sem passo.
/// </summary>
public sealed record ConsertoPendente(int EstruturaItemId, Local Local, decimal Quantidade);

/// <summary>
/// Um filho direto num Kit (spec da Fase 3B, secao 4.4). `Pronto`: o que aguarda coleta no ultimo passo dele.
/// `UltimoPasso` nulo: filho sem Roteiro, que nunca aguarda coleta. `JaNoDestino`: o ultimo passo e no Setor
/// onde o pai comeca (D2 da spec da Fase 3B).
/// </summary>
public sealed record FilhoDoKit(int FilhoId, decimal QuantidadePorPai, PassoDoCalculo? UltimoPasso, decimal Pronto, bool JaNoDestino);

/// <summary>`Conjuntos`: quantos conjuntos completos da para levar agora, sem passar do `Teto` (zero = incompleto).</summary>
public sealed record KitDaColeta(int PaiId, int SetorDeDestinoId, decimal Teto, decimal Conjuntos, IReadOnlyList<FilhoDoKit> Filhos)
{
  public bool Montavel => Conjuntos >= 1m;
}

/// <summary>
/// O que um plano ainda nao gravado tira de cada (no, posicao), para a calculadora responder sobre o "depois" (C15 da
/// spec da Fase 5C): o saldo depois e o saldo menos `Tirado(id, local)`, e o `FaltaMontar` depois de um pai e o
/// `FaltaMontar` menos `Tirado(pai, Local.AIniciar)`.
/// </summary>
public sealed record Retirada(Func<int, Local, decimal> Tirado);

/// <summary>
/// A regra da fila e das tarefas (spec da Fase 3, secao 7), em funcoes puras. Recebe os nos, os saldos
/// liquidos do livro, os totais montados e os passos ja alcancados; nao le nada. As escritas validam
/// com as MESMAS funcoes que a leitura mostra — e isso que fecha, por construcao, a tela oferecer
/// "montar 3" e a API recusar 3 (secao 7.2).
///
/// Enxerga so os nos que recebe: o destino e a tarefa de um Item precisam do PAI na entrada. Ver o
/// "Contrato de uso" do plano 2, Task 3.
///
/// Recebe tambem quais Setores tem `UtilizaKit`, para a regra do Kit (spec da Fase 3B), e o pronto de cada no (o
/// liquido das linhas `Pronto` do Retrabalho, decisao P3 do plano de backend da Fase 5B-2). O Pedido de fabricacao
/// nunca tem `Pronto`: sem essas entradas, nada muda para ele.
/// </summary>
public sealed class CalculadoraDeExecucao
{
  private static readonly IReadOnlyDictionary<Local, decimal> SemMovimento = new Dictionary<Local, decimal>();

  private readonly Dictionary<int, NoDoCalculo> _nos;
  private readonly ILookup<int, NoDoCalculo> _filhos;
  private readonly Dictionary<int, Dictionary<Local, decimal>> _liquido = new();
  private readonly IReadOnlyDictionary<int, decimal> _totaisMontados;
  private readonly Dictionary<int, HashSet<int>> _alcancados = new();
  private readonly IReadOnlySet<int> _setoresComKit;
  private readonly IReadOnlyDictionary<int, decimal> _perdidosDeAIniciar;
  private readonly IReadOnlySet<int> _saidosDoProjeto;
  private readonly IReadOnlyDictionary<int, decimal> _prontos;
  private readonly IReadOnlySet<int> _comProntoEmMontagem;

  /// <summary>As posicoes que nao sao producao: o que ja foi montado, expedido ou perdido (lista literal, nao derivada de `Posicoes.Todas`: posicao terminal nova tem de entrar aqui).</summary>
  private static readonly IReadOnlySet<string> ForaDaProducao =
      new HashSet<string> { Posicoes.Montado, Posicoes.Expedido, Posicoes.Perdido };

  public CalculadoraDeExecucao(
      IEnumerable<NoDoCalculo> nos,
      IEnumerable<SaldoLiquido> saldos,
      IReadOnlyDictionary<int, decimal> totaisMontados,
      IEnumerable<(int EstruturaItemId, int Ordem)> passosAlcancados,
      IReadOnlySet<int>? setoresComKit = null,
      IReadOnlyDictionary<int, decimal>? perdidosDeAIniciar = null,
      IReadOnlySet<int>? saidosDoProjeto = null,
      IReadOnlyDictionary<int, decimal>? prontos = null,
      IReadOnlySet<int>? comProntoEmMontagem = null)
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

    _setoresComKit = setoresComKit ?? new HashSet<int>();
    _perdidosDeAIniciar = perdidosDeAIniciar ?? new Dictionary<int, decimal>();
    _saidosDoProjeto = saidosDoProjeto ?? new HashSet<int>();
    _prontos = prontos ?? new Dictionary<int, decimal>();
    _comProntoEmMontagem = comProntoEmMontagem ?? new HashSet<int>();
  }

  public IEnumerable<NoDoCalculo> Nos => _nos.Values.OrderBy(n => n.Id);

  public NoDoCalculo No(int id) => _nos[id];

  public IReadOnlyList<NoDoCalculo> Filhos(int paiId) => _filhos[paiId].ToList();

  /// <summary>
  /// Os filhos que ainda contam para a montagem do pai: todos, menos os que sairam do projeto (C22 da spec da Fase
  /// 5B-1, decisao P2 do plano de backend). `Filhos` continua inteira, para a arvore da tela.
  /// </summary>
  public IReadOnlyList<NoDoCalculo> FilhosQueContam(int paiId) =>
      _filhos[paiId].Where(f => !_saidosDoProjeto.Contains(f.Id)).ToList();

  /// <summary>O filho descartado por inteiro (baixa `Tudo` de motivo `Descarte`, nao estornada, no proprio no).</summary>
  public bool SaiuDoProjeto(int id) => _saidosDoProjeto.Contains(id);

  public bool TemFilhos(int id) => _filhos[id].Any();

  /// <summary>
  /// O no que a Fila oferece a iniciar sozinho, como uma folha: sem filhos, ou com filhos dos quais nenhum conta mais
  /// para a montagem, porque todos sairam do projeto (C22 da spec da Fase 5B-1). Vale para Avulso e para Kit (C9 da
  /// spec da Fase 5C): sem filho a receber, a regra 25 nao tem o que validar.
  /// </summary>
  public bool IniciaComoFolha(int id) => FilhosQueContam(id).Count == 0;

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

  /// <summary>
  /// O que o "Usar a sobra" pos pronto no no do Retrabalho: o liquido das linhas `Pronto`, menos os estornos delas
  /// (decisao P3 do plano de backend da Fase 5B-2). As unidades ja estao numa posicao de verdade (a mesma da sobra no
  /// original, secao 3.1 da spec da Fase 5B-2), entao os saldos ja as veem; isto so diz quantas vieram assim.
  /// </summary>
  public decimal Pronto(int id) => _prontos.GetValueOrDefault(id);

  /// <summary>
  /// As montagens que valem, mais o pronto quando o no tem filhos (C17 da spec da Fase 5B-2): o pai pronto ja chegou
  /// montado, como se tivesse consumido os filhos. Assim o que saiu de "a iniciar" do pai continua igual ao total
  /// montado dele, a identidade da Fase 3D. O pronto de uma folha nao e montagem e nao entra.
  /// </summary>
  public decimal TotalMontado(int id) =>
      _totaisMontados.GetValueOrDefault(id) + (TemFilhos(id) ? Pronto(id) : 0m);

  /// <summary>O liquido da baixa que saiu de "a iniciar" do no (C5 da spec da Fase 5B-1), ja sem os estornos.</summary>
  public decimal PerdidoDeAIniciar(int id) => _perdidosDeAIniciar.GetValueOrDefault(id);

  /// <summary>
  /// A soma dos saldos em producao: todas as posicoes menos `Montado`, `Expedido` e `Perdido`. A perda depois do
  /// inicio nao entra em `FaltaMontar`, mas sai daqui.
  /// </summary>
  public decimal EmProducao(int id) =>
      Saldos(id).Where(s => !ForaDaProducao.Contains(s.Local.Posicao)).Sum(s => s.Quantidade);

  /// <summary>
  /// O que o pai ainda tem de montar: quantidade, menos o montado, menos o que se perdeu em "a iniciar" (C5 da spec
  /// da Fase 5B-1; decisao P2 do plano de backend). A perda depois do inicio nao desconta: aquelas unidades ja
  /// consumiram os filhos.
  /// </summary>
  public decimal FaltaMontar(int paiId) => _nos[paiId].Quantidade - TotalMontado(paiId) - PerdidoDeAIniciar(paiId);

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
  /// O que o pai ainda precisa receber deste filho (spec da Fase 3, secao 7.4, com a C5 da spec da Fase 5C):
  /// `max(0, FaltaMontar(P) x QuantidadePorPai(c) - (soma de AguardandoMontagem(c) - ExcessoEmMontagem(c)))`. A sobra em
  /// montagem nao cobre o pai: sem desconta-la, o resto de um conjunto quebrado reduziria a tarefa da coleta. No pai
  /// que nao recebe em conjunto, da o mesmo que sem o desconto.
  /// </summary>
  public decimal Precisa(int filhoId)
  {
    var filho = _nos[filhoId];
    if (filho.PaiId is not int paiId || !_nos.ContainsKey(paiId)) return 0m;
    return Math.Max(0m,
        FaltaMontar(paiId) * Razao(filho) - (AguardandoMontagemTotal(filhoId) - ExcessoEmMontagem(filhoId)));
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

  public DestinoCalculado DestinoDaColeta(int id, int ordem)
  {
    if (ProximoPasso(id, ordem) is PassoDoCalculo proximo)
      return new DestinoCalculado(TipoDeDestino.ProximoPasso, proximo, null);

    if (_nos[id].PaiId is not int paiId)
      return new DestinoCalculado(TipoDeDestino.Expedicao, null, null);

    return new DestinoCalculado(TipoDeDestino.Montagem, PrimeiroPassoDoPai(paiId), paiId);
  }

  /// <summary>O pai precisa estar entre os nos recebidos (o "Contrato de uso" da calculadora); se nao estiver, sem destino.</summary>
  private PassoDoCalculo? PrimeiroPassoDoPai(int paiId) =>
      _nos.TryGetValue(paiId, out var pai) && pai.Roteiro.Count > 0 ? pai.Roteiro[0] : null;

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

  /// <summary>
  /// A sobra em montagem, no nivel do no (spec da Fase 3, secao 7.5; C4 e C16 da spec da Fase 5C). No filho de pai que
  /// nao recebe em conjunto, o que aguarda montagem alem do que o pai precisa: `max(0, espera - FaltaMontar x razao)`.
  /// No filho de pai que recebe em conjunto, com `contados = min(ConjuntosAEspera, max(0, FaltaMontar))`: o que espera
  /// em Setor com `UtilizaKit` e nao esta num conjunto contado, `max(0, espera - contados x razao)`, mais o que espera
  /// fora deles alem do que o pai ainda precisa depois dos contados,
  /// `max(0, esperaForaDoKit - max(0, FaltaMontar - contados) x razao)`. `retirada` aplica um plano ainda nao gravado
  /// as esperas e ao `FaltaMontar` do pai.
  /// </summary>
  public decimal ExcessoEmMontagem(int filhoId, Retirada? retirada = null)
  {
    var (noKit, resto) = SobraEmMontagem(filhoId, retirada);
    return noKit + resto;
  }

  /// <summary>
  /// A sobra em montagem repartida por Setor (C16 da spec da Fase 5C), so com os Setores em que ha sobra. No filho de
  /// pai que recebe em conjunto: primeiro o que nao esta em conjunto contado, pelos Setores com `UtilizaKit` em ordem
  /// de Id, ate o saldo de cada um; depois o termo de fora, pelos Setores sem `UtilizaKit`, na mesma ordem. Nos outros,
  /// o `ExcessoEmMontagem` por todos os Setores em ordem de Id.
  /// </summary>
  public IReadOnlyList<(int SetorId, decimal Quantidade)> SobraEmMontagemPorSetor(int filhoId)
  {
    var (noKit, resto) = SobraEmMontagem(filhoId, null);
    var setores = SetoresOndeAguardaMontagem(filhoId);
    var emConjunto = _nos[filhoId].PaiId is int paiId && RecebeEmConjunto(paiId);
    var lista = new List<(int SetorId, decimal Quantidade)>();

    void Repartir(decimal sobra, IEnumerable<int> pelos)
    {
      foreach (var setorId in pelos)
      {
        if (sobra <= 0m) break;
        var daqui = Math.Min(sobra, AguardandoMontagem(filhoId, setorId));
        if (daqui <= 0m) continue;
        lista.Add((setorId, daqui));
        sobra -= daqui;
      }
    }

    if (emConjunto)
    {
      Repartir(noKit, setores.Where(s => _setoresComKit.Contains(s)));
      Repartir(resto, setores.Where(s => !_setoresComKit.Contains(s)));
    }
    else
      Repartir(resto, setores);
    return lista;
  }

  /// <summary>
  /// Os dois termos da sobra em montagem: `NoKit`, o que espera em Setor com `UtilizaKit` fora de conjunto contado (so
  /// no filho de pai que recebe em conjunto), e `Resto`, o que espera alem do que o pai precisa (nesse pai, so a espera
  /// fora de Setor com `UtilizaKit`; nos outros, toda a espera).
  /// </summary>
  private (decimal NoKit, decimal Resto) SobraEmMontagem(int filhoId, Retirada? retirada)
  {
    var filho = _nos[filhoId];
    if (filho.PaiId is not int paiId || !_nos.ContainsKey(paiId)) return (0m, 0m);
    var razao = Razao(filho);
    var falta = FaltaMontar(paiId) - (retirada?.Tirado(paiId, Local.AIniciar) ?? 0m);
    var noKit = EmEsperaNoKit(filhoId, 0m, retirada);
    var fora = EmEsperaForaDoKit(filhoId, retirada);
    if (!RecebeEmConjunto(paiId)) return (0m, Math.Max(0m, noKit + fora - falta * razao));

    var (completos, emFormacao, _) = Formacao(paiId, null, retirada);
    var contados = Math.Min(completos + emFormacao, Math.Max(0m, falta));
    return (Math.Max(0m, noKit - contados * razao),
        Math.Max(0m, fora - Math.Max(0m, falta - contados) * razao));
  }

  /// <summary>
  /// "Da para montar N; falta X de Y" no Setor (spec secao 7.6):
  /// N = min(falta montar, min por filho de floor(presente / razao)); o "falta" e o que cada filho
  /// precisa para a unidade N+1, so enquanto N+1 cabe no que falta montar. No pai que recebe em conjunto, o "falta"
  /// conta o presente menos a sobra que esta neste Setor (C6 da spec da Fase 5C), para a Fila nao convidar a completar
  /// um conjunto quebrado; o `Presente` devolvido continua o saldo inteiro.
  /// </summary>
  public Montabilidade CalcularMontabilidade(int paiId, int setorId)
  {
    var falta = Math.Max(0m, FaltaMontar(paiId));
    var filhos = FilhosQueContam(paiId);
    // Sem filho que conte (todos descartados por inteiro), o pai monta o que falta: nada limita (C22 da 5B-1, C9 da 5C).
    if (filhos.Count == 0)
      return new Montabilidade(paiId, setorId, falta, IniciaComoFolha(paiId) ? falta : 0m, Array.Empty<FilhoNaMontagem>());

    var n = falta;
    foreach (var c in filhos)
    {
      var razao = Razao(c);
      var possivel = razao == 0m ? 0m : Math.Floor(AguardandoMontagem(c.Id, setorId) / razao);
      n = Math.Min(n, possivel);
    }

    var proxima = n + 1m;
    var haProxima = proxima <= falta;
    var emConjunto = RecebeEmConjunto(paiId);
    var lista = filhos.Select(c =>
    {
      var razao = Razao(c);
      var presente = AguardandoMontagem(c.Id, setorId);
      var sobraAqui = emConjunto
          ? SobraEmMontagemPorSetor(c.Id).Where(s => s.SetorId == setorId).Sum(s => s.Quantidade)
          : 0m;
      decimal? necessario = haProxima ? proxima * razao : null;
      decimal? faltaParaProxima = haProxima ? Math.Max(0m, proxima * razao - (presente - sobraAqui)) : null;
      return new FilhoNaMontagem(c.Id, razao, presente, necessario, faltaParaProxima);
    }).ToList();

    return new Montabilidade(paiId, setorId, falta, n, lista);
  }

  public bool SetorUtilizaKit(int setorId) => _setoresComKit.Contains(setorId);

  /// <summary>
  /// Regra 25 (D3 da spec da Fase 3B): o pai de Agrupamento Kit, com filhos, cujo PRIMEIRO passo e num Setor com
  /// `UtilizaKit` recebe os filhos so em conjuntos completos.
  /// </summary>
  public bool RecebeEmConjunto(int paiId) =>
      _nos.TryGetValue(paiId, out var pai) && pai.DeKit && TemFilhos(paiId)
      && PrimeiroPasso(paiId) is PassoDoCalculo primeiro && _setoresComKit.Contains(primeiro.SetorId);

  /// <summary>
  /// O que o filho aguarda montagem em Setores com `UtilizaKit`, menos o que esta saindo de la (`saindo`, de um
  /// redirecionamento) e o que um plano ainda nao gravado tira (`retirada`).
  /// </summary>
  private decimal EmEsperaNoKit(int filhoId, decimal saindo, Retirada? retirada = null) =>
      EmEspera(filhoId, noKit: true, retirada) - saindo;

  /// <summary>O mesmo, em Setores sem `UtilizaKit` (C16 da spec da Fase 5C).</summary>
  private decimal EmEsperaForaDoKit(int filhoId, Retirada? retirada) => EmEspera(filhoId, noKit: false, retirada);

  private decimal EmEspera(int filhoId, bool noKit, Retirada? retirada) =>
      LiquidoDo(filhoId)
          .Where(kv => kv.Key.Posicao == Posicoes.AguardandoMontagem && _setoresComKit.Contains(kv.Key.SetorId!.Value) == noKit)
          .Sum(kv => kv.Value - (retirada?.Tirado(filhoId, kv.Key) ?? 0m));

  /// <summary>
  /// Os conjuntos que entraram e ainda nao foram montados (regra 25; C2 da spec da Fase 5C): os completos, o menor
  /// `floor(espera / razao)` entre os filhos que contam, mais os em formacao do pai com `Pronto` em montagem (D8 da
  /// spec da Fase 5B-2), que sao zero em qualquer outro pai. O que espera e nao fecha conjunto nao ocupa vaga: e sobra
  /// (`ExcessoEmMontagem`). `saindo` (filho -> quantidade) e o que um redirecionamento tira da espera de um Setor com
  /// `UtilizaKit`: ja estava contado, nao conta de novo.
  /// </summary>
  public decimal ConjuntosAEspera(int paiId, IReadOnlyDictionary<int, decimal>? saindo = null)
  {
    var (completos, emFormacao, _) = Formacao(paiId, saindo, null);
    return completos + emFormacao;
  }

  /// <summary>
  /// O pai recebe em conjunto e tem algum filho que conta aguardando montagem em Setor com `UtilizaKit` (C8 da spec
  /// da Fase 5C: e o que barra mudar a estrutura dele).
  /// </summary>
  public bool TemConjuntoAEspera(int paiId) =>
      RecebeEmConjunto(paiId) && FilhosQueContam(paiId).Any(f => EmEsperaNoKit(f.Id, 0m) > 0m);

  /// <summary>
  /// Os conjuntos em formacao do pai que ainda valem (D8 da spec da Fase 5B-2; decisao P4 do plano de backend, limitada
  /// pela regra 25): no pai que recebe em conjunto e tem algum filho que chegou a montagem por um `Pronto`, os conjuntos a
  /// espera que ainda nao estao completos, `ConjuntosAEspera - completos`, limitados ao que o pai ainda precisa receber
  /// alem dos completos, `max(0, FaltaMontar - completos)` (regra 25: nunca alem do que o no ainda precisa). O excesso
  /// aparece quando o pai perde "a iniciar" depois do pronto. Sem `Pronto` em montagem, zero.
  /// </summary>
  public decimal ConjuntosEmFormacao(int paiId) => Formacao(paiId, null, null).QueValem;

  /// <summary>
  /// O que o filho pode entrar alem dos conjuntos completos, para completar os conjuntos em formacao que valem (D8 da
  /// spec da Fase 5B-2; decisao P4 do plano de backend, limitada pela regra 25):
  /// `max(0, (completos + ConjuntosEmFormacao) x razao - espera do filho)`, so enquanto ha conjunto em formacao. Os
  /// conjuntos em formacao ja estao na espera, entao o complemento nao ocupa o teto; e, como eles nao passam do
  /// `FaltaMontar`, o que entra pelo complemento tambem nao passa do que o pai ainda precisa.
  /// </summary>
  public decimal ComplementoDoConjunto(int paiId, int filhoId)
  {
    var (completos, _, queValem) = Formacao(paiId, null, null);
    if (queValem <= 0m) return 0m;
    if (FilhosQueContam(paiId).FirstOrDefault(f => f.Id == filhoId) is not NoDoCalculo filho) return 0m;
    return Math.Max(0m, (completos + queValem) * Razao(filho) - EmEsperaNoKit(filhoId, 0m));
  }

  /// <summary>
  /// Os termos dos conjuntos a espera de um pai. `Completos`: o menor `floor(espera / razao)` entre os filhos que
  /// contam (zero sem nenhum), para todo pai. `EmFormacao`: so no pai que recebe em conjunto e tem algum filho que
  /// chegou a montagem por um `Pronto` (D8 da spec da Fase 5B-2), os conjuntos do filho mais adiantado, o maior
  /// `ceil(espera / razao)`, alem dos completos; este e o unico lugar em que a espera arredonda para cima. `QueValem`:
  /// os em formacao limitados ao que o pai ainda precisa receber alem dos completos (regra 25).
  /// </summary>
  private (decimal Completos, decimal EmFormacao, decimal QueValem) Formacao(
      int paiId, IReadOnlyDictionary<int, decimal>? saindo, Retirada? retirada)
  {
    var filhos = FilhosQueContam(paiId);
    if (filhos.Count == 0) return (0m, 0m, 0m);

    var completos = decimal.MaxValue;
    var maisAdiantado = 0m;
    foreach (var filho in filhos)
    {
      var razao = Razao(filho);
      var espera = Math.Max(0m, EmEsperaNoKit(filho.Id, saindo?.GetValueOrDefault(filho.Id) ?? 0m, retirada));
      completos = Math.Min(completos, razao <= 0m ? 0m : Math.Floor(espera / razao));
      if (razao > 0m) maisAdiantado = Math.Max(maisAdiantado, Math.Ceiling(espera / razao));
    }
    if (!RecebeEmConjunto(paiId) || !filhos.Any(f => _comProntoEmMontagem.Contains(f.Id))) return (completos, 0m, 0m);

    var emFormacao = Math.Max(0m, maisAdiantado - completos);
    var falta = FaltaMontar(paiId) - (retirada?.Tirado(paiId, Local.AIniciar) ?? 0m);
    return (completos, emFormacao, Math.Min(emFormacao, Math.Max(0m, falta - completos)));
  }

  /// <summary>
  /// Quantos conjuntos o pai ainda precisa receber (regra 25; C3 da spec da Fase 5C): o que falta montar, menos os
  /// conjuntos a espera.
  /// </summary>
  public decimal TetoDeEntrada(int paiId, IReadOnlyDictionary<int, decimal>? saindo = null) =>
      Math.Max(0m, FaltaMontar(paiId) - ConjuntosAEspera(paiId, saindo));

  private PassoDoCalculo? UltimoPasso(int id) => _nos[id].Roteiro.Count == 0 ? null : _nos[id].Roteiro[^1];

  /// <summary>O filho de Kit que aguarda coleta no ULTIMO passo vai para o cartao do Kit, nao para o "Item pronto".</summary>
  private bool VaiNoKit(int id, int ordem) =>
      _nos[id].PaiId is int paiId && ProximoPasso(id, ordem) is null && RecebeEmConjunto(paiId);

  /// <summary>
  /// Os Kits que as Tarefas mostram (regra 23; spec da Fase 3B, secao 4.4): todo pai que recebe em conjunto, com
  /// teto maior que zero e algum filho pronto. `Conjuntos` = min(teto, min por filho de floor(pronto / razao)). O que
  /// completa um conjunto em formacao vai nas coletas (`ColetasPendentes`) e sai do pronto do cartao, para a mesma
  /// unidade nao ser oferecida duas vezes (D8 da spec da Fase 5B-2).
  /// </summary>
  public IReadOnlyList<KitDaColeta> KitsDaColeta()
  {
    var lista = new List<KitDaColeta>();
    foreach (var pai in Nos)
    {
      if (!RecebeEmConjunto(pai.Id)) continue;
      var teto = TetoDeEntrada(pai.Id);
      if (teto <= 0m) continue;
      var destino = PrimeiroPasso(pai.Id)!.Value.SetorId;
      var filhos = FilhosQueContam(pai.Id).Select(c =>
      {
        var ultimo = UltimoPasso(c.Id);
        var naColeta = ultimo is PassoDoCalculo u ? Math.Max(0m, Saldo(c.Id, Local.AguardandoColeta(u.SetorId, u.Ordem))) : 0m;
        var pronto = Math.Max(0m, naColeta - ComplementoDoConjunto(pai.Id, c.Id));
        return new FilhoDoKit(c.Id, Razao(c), ultimo, pronto, ultimo?.SetorId == destino);
      }).ToList();
      if (filhos.All(f => f.Pronto <= 0m)) continue;

      var conjuntos = Math.Floor(teto);
      foreach (var f in filhos)
        conjuntos = Math.Min(conjuntos, f.QuantidadePorPai <= 0m ? 0m : Math.Floor(f.Pronto / f.QuantidadePorPai));
      lista.Add(new KitDaColeta(pai.Id, destino, teto, conjuntos, filhos));
    }
    return lista;
  }

  /// <summary>
  /// Todo (no, passo) que aguarda coleta com tarefa positiva, por Setor, no e passo. Sem os filhos de Kit no
  /// ultimo passo, que vao no cartao do Kit (`KitsDaColeta`) — menos o complemento de um conjunto em formacao, que
  /// entra aqui sozinho, ate o que o conjunto pede (D8 da spec da Fase 5B-2).
  /// </summary>
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
        if (VaiNoKit(id, local.Ordem!.Value))
          tarefa = Math.Min(tarefa, ComplementoDoConjunto(_nos[id].PaiId!.Value, id));
        if (tarefa > 0m) lista.Add(new ColetaPendente(id, local.SetorId.Value, local.Ordem.Value, tarefa));
      }
    }

    return lista.OrderBy(c => c.SetorId).ThenBy(c => c.EstruturaItemId).ThenBy(c => c.Ordem).ToList();
  }

  /// <summary>
  /// Toda posicao do conserto que espera o Movimentador (`AguardandoConserto` e `ConsertoPronto`) com saldo positivo,
  /// por Setor, no e posicao. `EmConserto` fica de fora: quem a move e o Operador, pela Fila.
  /// </summary>
  public IReadOnlyList<ConsertoPendente> ConsertosPendentes()
  {
    var lista = new List<ConsertoPendente>();
    foreach (var (id, locais) in _liquido)
    {
      if (!_nos.ContainsKey(id)) continue;
      foreach (var local in locais.Keys)
      {
        if (local.Posicao is not (Posicoes.AguardandoConserto or Posicoes.ConsertoPronto)) continue;
        var quantidade = Saldo(id, local);
        if (quantidade > 0m) lista.Add(new ConsertoPendente(id, local, quantidade));
      }
    }

    return lista.OrderBy(c => c.Local.SetorId).ThenBy(c => c.EstruturaItemId)
        .ThenBy(c => Livro.OrdemDaPosicao(c.Local.Posicao)).ToList();
  }
}

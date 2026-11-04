using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Baixa;

/// <summary>
/// A conta da baixa (secoes 4.1 a 4.3 e 4.6 da spec da Fase 5B-1), pura: recebe a calculadora com o estado ANTES da
/// baixa e devolve o que sai, sem gravar nada. A escrita a chama dentro da transacao, depois das travas, e a previa
/// fora dela; e a mesma conta para as duas (decisao P4 do plano de backend da Fase 5B-1).
///
/// A calculadora tem de enxergar a arvore inteira do no ate as folhas e a cadeia ate a Peca do topo; com
/// `IncluirTopo`, a arvore inteira da Peca, porque a cascata desce dela.
///
/// Motivo e observacao sao do caso de uso, nao daqui: esta conta nao olha o motivo.
/// </summary>
public static class CalculoDaBaixa
{
  public static Result<PlanoDaBaixa> Planejar(
      CalculadoraDeExecucao calc, PedidoDeBaixa pedido, Func<int, string> nome, Func<int, string> nomeDoSetor)
  {
    var no = calc.No(pedido.NoId);
    if (pedido.CadeiaAteOTopo.Count > 0 && pedido.CadeiaAteOTopo[0] != no.PaiId)
      throw new ArgumentException("A cadeia ate o topo comeca no pai do no.", nameof(pedido));

    if (pedido.IncluirTopo && (pedido.Tudo || no.EhPeca || pedido.CadeiaAteOTopo.Count == 0))
      return Falhas.Validacao<PlanoDaBaixa>(CodigosDaBaixa.OrigemInvalida,
          "Só a baixa de uma parte de um Item leva junto a Peça do topo.");

    return pedido.Tudo ? PlanejarTudo(calc, pedido, nome) : PlanejarParte(calc, pedido, nome, nomeDoSetor);
  }

  /// <summary>
  /// O descarte total (D11): o no e cada descendente, em ordem de Id, uma linha por posicao de onde se da baixa com
  /// saldo positivo. A cascata nao e necessaria, porque tudo da arvore sai, e nada vira sobra.
  /// </summary>
  private static Result<PlanoDaBaixa> PlanejarTudo(CalculadoraDeExecucao calc, PedidoDeBaixa pedido, Func<int, string> nome)
  {
    if (pedido.Origens.Count > 0)
      return Falhas.Validacao<PlanoDaBaixa>(CodigosDaBaixa.OrigemInvalida,
          "Dar baixa de tudo não escolhe de onde tira: leva todas as posições.");

    var ids = Subarvore(calc, pedido.NoId).OrderBy(id => id).ToList();

    // C15 da spec da Fase 5B-1: a unidade presa a um pedido de liberacao nao sai; o pedido se estorna antes.
    foreach (var id in ids)
    {
      var aguardando = calc.Saldo(id, Local.AguardandoLiberacao);
      if (aguardando > 0m)
        return Falhas.Conflito<PlanoDaBaixa>(CodigosDaExpedicao.LiberacaoEmAberto,
            $"{nome(id)} tem {Quantidades.Formatar(aguardando)} aguardando liberação: "
            + "estorne o pedido de liberação antes de descartar tudo.");
    }

    var linhas = ids
        .SelectMany(id => calc.Saldos(id)
            .Where(s => Posicoes.DeOndeSeDaBaixa.Contains(s.Local.Posicao) && s.Quantidade > 0m)
            .Select(s => new LinhaPlanejada(id, s.Local, s.Quantidade)))
        .ToList();
    if (linhas.Count == 0)
      return Falhas.Conflito<PlanoDaBaixa>(CodigosDaBaixa.NadaEmProducao,
          $"{nome(pedido.NoId)} não tem nada em produção para dar baixa.");

    return Result<PlanoDaBaixa>.Ok(new PlanoDaBaixa(linhas, [], null));
  }

  /// <summary>
  /// A baixa de parte (D3, D4): as origens pedidas, depois a cascata C6 a partir do no, a trava no topo quando o no e
  /// Item, e, com `IncluirTopo`, a Peca do topo de "a iniciar" com a cascata de novo a partir dela (decisao P10 do plano
  /// de backend da Fase 5B-1).
  /// </summary>
  private static Result<PlanoDaBaixa> PlanejarParte(
      CalculadoraDeExecucao calc, PedidoDeBaixa pedido, Func<int, string> nome, Func<int, string> nomeDoSetor)
  {
    if (pedido.Origens.Count == 0)
      return Falhas.Validacao<PlanoDaBaixa>(CodigosDaBaixa.OrigemInvalida, "Diga de onde sai a baixa.");

    // Primeiro o que e erro do pedido (400), depois o que depende do saldo (409).
    var vistas = new HashSet<Local>();
    foreach (var origem in pedido.Origens)
    {
      if (RecusaDaOrigem(origem.Local) is string recusa)
        return Falhas.Validacao<PlanoDaBaixa>(CodigosDaBaixa.OrigemInvalida, recusa);
      if (!vistas.Add(origem.Local))
        return Falhas.Validacao<PlanoDaBaixa>(CodigosDaExecucao.ItemRepetido,
            "A mesma posição veio duas vezes na baixa: junte as quantidades numa linha só.");
      if (origem.Quantidade <= 0m || !Quantidades.CabeNaColuna(origem.Quantidade))
        return Falhas.QuantidadeInvalida<PlanoDaBaixa>(origem.Quantidade);
    }

    foreach (var origem in pedido.Origens)
    {
      var saldo = calc.Saldo(pedido.NoId, origem.Local);
      if (origem.Quantidade > saldo)
        return Falhas.Conflito<PlanoDaBaixa>(CodigosDaExecucao.SaldoInsuficiente,
            $"Só há {Quantidades.Formatar(Math.Max(0m, saldo))} de {nome(pedido.NoId)} "
            + $"{RotuloDoLocal.Onde(origem.Local, nomeDoSetor)}.");
    }

    var rascunho = new Rascunho(calc);
    foreach (var origem in pedido.Origens) rascunho.Tirar(pedido.NoId, origem.Local, origem.Quantidade);
    rascunho.Cascata(pedido.NoId);

    var trava = pedido.CadeiaAteOTopo.Count > 0 ? rascunho.TravaNoTopo(pedido.NoId, pedido.CadeiaAteOTopo) : null;
    if (pedido.IncluirTopo && trava is not null)
    {
      var doTopo = Math.Min(trava.Quantidade, trava.AIniciar);
      if (doTopo > 0m)
      {
        rascunho.Tirar(trava.PecaId, Local.AIniciar, doTopo);
        rascunho.Cascata(trava.PecaId);
      }
    }

    return Result<PlanoDaBaixa>.Ok(new PlanoDaBaixa(rascunho.Linhas, rascunho.ViramSobra(), trava));
  }

  /// <summary>
  /// Por que a origem nao serve (secao 4.2 da spec da Fase 5B-1), ou nulo. Tambem o Setor e o passo incoerentes com a
  /// posicao (secao 5.4): as posicoes do local de expedicao e o "a iniciar" nao tem nenhum dos dois, o Setor e a coleta
  /// tem os dois, a espera da montagem e o conserto so o Setor.
  /// </summary>
  private static string? RecusaDaOrigem(Local local)
  {
    if (local.Posicao == Posicoes.AguardandoLiberacao)
      return "A unidade aguardando liberação está presa ao pedido de liberação: estorne o pedido antes de dar baixa.";
    if (!Posicoes.DeOndeSeDaBaixa.Contains(local.Posicao))
      return "Não se dá baixa do que já foi montado, expedido ou perdido: só do que está em produção.";

    var coerente = local.Posicao switch
    {
      Posicoes.NoSetor or Posicoes.AguardandoColeta => local.SetorId is not null && local.Ordem is not null,
      Posicoes.AguardandoMontagem or Posicoes.AguardandoConserto or Posicoes.EmConserto or Posicoes.ConsertoPronto =>
          local.SetorId is not null && local.Ordem is null,
      _ => local.SetorId is null && local.Ordem is null,
    };
    return coerente ? null : "O Setor ou o passo informado não combina com a posição de onde sai a baixa.";
  }

  private static IEnumerable<int> Subarvore(CalculadoraDeExecucao calc, int raiz)
  {
    var fila = new Queue<int>([raiz]);
    while (fila.Count > 0)
    {
      var id = fila.Dequeue();
      yield return id;
      foreach (var filho in calc.Filhos(id)) fila.Enqueue(filho.Id);
    }
  }

  private static decimal Razao(NoDoCalculo no) => no.QuantidadePorPai ?? 0m;

  /// <summary>
  /// As linhas sendo planejadas, sobre o estado de antes: uma linha por (no, posicao), somando o que entra de novo na
  /// mesma, na ordem em que nasceram. "Depois" e o estado de antes menos o que ja esta planejado.
  /// </summary>
  private sealed class Rascunho
  {
    private readonly CalculadoraDeExecucao _calc;
    private readonly List<LinhaPlanejada> _linhas = new();

    public Rascunho(CalculadoraDeExecucao calc) => _calc = calc;

    public IReadOnlyList<LinhaPlanejada> Linhas => _linhas;

    public void Tirar(int noId, Local origem, decimal quantidade)
    {
      var i = _linhas.FindIndex(l => l.NoId == noId && l.Origem == origem);
      if (i < 0) _linhas.Add(new LinhaPlanejada(noId, origem, quantidade));
      else _linhas[i] = _linhas[i] with { Quantidade = _linhas[i].Quantidade + quantidade };
    }

    private decimal Tirado(int noId) => _linhas.Where(l => l.NoId == noId).Sum(l => l.Quantidade);

    /// <summary>O "a iniciar" planejado do no: e o que, somado ao ja gravado, sai do que ele falta montar.</summary>
    private decimal PerdidoDeAIniciar(int noId) =>
        _linhas.Where(l => l.NoId == noId && l.Origem == Local.AIniciar).Sum(l => l.Quantidade);

    private decimal EmProducaoDepois(int noId) => _calc.EmProducao(noId) - Tirado(noId);

    private decimal AIniciarDepois(int noId) => _calc.Saldo(noId, Local.AIniciar) - PerdidoDeAIniciar(noId);

    /// <summary>
    /// O que o pai ainda pede do filho depois da baixa: o que falta montar do pai (que ja desconta a perda gravada),
    /// menos o "a iniciar" planejado dele, vezes a razao.
    /// </summary>
    private decimal DeveDepois(int paiId, NoDoCalculo filho) =>
        Math.Max(0m, _calc.FaltaMontar(paiId) - PerdidoDeAIniciar(paiId)) * Razao(filho);

    private decimal DeveAntes(int paiId, NoDoCalculo filho) => Math.Max(0m, _calc.FaltaMontar(paiId)) * Razao(filho);

    /// <summary>
    /// C6 da spec da Fase 5B-1, por nivel: em cada filho, o "a iniciar" alem do que o pai agora pede sai junto, e o
    /// filho que perdeu entra no nivel seguinte. Roda sobre o "depois", entao rodar de novo a partir de outro no (a Peca
    /// do topo) nao conta duas vezes o que ja saiu.
    /// </summary>
    public void Cascata(int inicio)
    {
      var nivel = new List<int> { inicio };
      while (nivel.Count > 0)
      {
        var proximo = new List<int>();
        foreach (var paiId in nivel)
          foreach (var filho in _calc.Filhos(paiId))
          {
            var excesso = Math.Min(
                AIniciarDepois(filho.Id), Math.Max(0m, EmProducaoDepois(filho.Id) - DeveDepois(paiId, filho)));
            // O livro guarda quatro casas: a razao com casas pode dar uma quinta, e o que sai arredonda para baixo.
            excesso = decimal.Floor(excesso * 10_000m) / 10_000m;
            if (excesso <= 0m) continue;
            Tirar(filho.Id, Local.AIniciar, excesso);
            proximo.Add(filho.Id);
          }
        nivel = proximo;
      }
    }

    /// <summary>
    /// Quantas unidades da Peca do topo a baixa trava (secao 4.6 da spec da Fase 5B-1), com a baixa ja aplicada. Em cada
    /// pai da cadeia, as unidades que ainda faltam montar e que o filho em producao ja nao cobre sao
    /// `FaltaMontar(pai) - floor(producao do filho / razao do filho)`. Subindo, a "producao do filho" de um nivel e o em
    /// producao dele menos as unidades dele que ficaram travadas: o que o nivel tem alem do que o de cima precisa
    /// absorve a trava, em vez de ela passar inteira (so dividir a trava pela razao, `ceil(trava / razao)`, da o mesmo
    /// apenas quando o nivel nao tem nada alem do que o de cima precisa). Nulo quando nada trava.
    /// </summary>
    public TravaNoTopo? TravaNoTopo(int noId, IReadOnlyList<int> cadeia)
    {
      var filho = _calc.No(noId);
      var producaoDoFilho = EmProducaoDepois(noId);
      var trava = 0m;
      foreach (var paiId in cadeia)
      {
        var razao = Razao(filho);
        if (razao <= 0m) return null;
        trava = Math.Max(0m, Math.Max(0m, _calc.FaltaMontar(paiId)) - Math.Floor(producaoDoFilho / razao));
        if (trava <= 0m) return null;
        producaoDoFilho = Math.Max(0m, _calc.EmProducao(paiId) - trava);
        filho = _calc.No(paiId);
      }

      var pecaId = cadeia[^1];
      return new TravaNoTopo(pecaId, trava, _calc.Saldo(pecaId, Local.AIniciar));
    }

    /// <summary>
    /// O fabricado que passa a sobrar por causa da baixa (C6): em cada filho de um no que perdeu "a iniciar", o que ele
    /// tem fora do "a iniciar" alem do que o pai pede, depois menos antes. A sobra que ja existia nao entra.
    /// </summary>
    public IReadOnlyList<(int NoId, decimal Quantidade)> ViramSobra()
    {
      var lista = new List<(int NoId, decimal Quantidade)>();
      var pais = _linhas.Where(l => l.Origem == Local.AIniciar).Select(l => l.NoId).Distinct();
      foreach (var paiId in pais)
        foreach (var filho in _calc.Filhos(paiId))
        {
          var fabricadoAntes = _calc.EmProducao(filho.Id) - _calc.Saldo(filho.Id, Local.AIniciar);
          var fabricadoDepois = EmProducaoDepois(filho.Id) - AIniciarDepois(filho.Id);
          var antes = Math.Max(0m, fabricadoAntes - DeveAntes(paiId, filho));
          var depois = Math.Max(0m, fabricadoDepois - DeveDepois(paiId, filho));
          if (depois > antes) lista.Add((filho.Id, depois - antes));
        }
      return lista.OrderBy(s => s.NoId).ToList();
    }
  }
}

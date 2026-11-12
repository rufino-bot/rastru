using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Retrabalho;

/// <summary>
/// A sobra viva de um no do original (regra 30), numa posicao, oferecida ao no do Retrabalho que veio dele (C14 da spec
/// da Fase 5B-2).
/// </summary>
public sealed record SobraDisponivel(int NoDoRetrabalhoId, int NoDeOrigemId, Local Local, decimal Sobra);

/// <summary>O que o PCP pediu: tantas unidades da sobra naquela posicao viram pronto no no do Retrabalho.</summary>
public sealed record ItemReaproveitado(int NoDoRetrabalhoId, Local Local, decimal Quantidade);

/// <summary>
/// A conta do "Usar a sobra" (secao 4.4 da spec da Fase 5B-2), pura: o que o original tem de sobra viva por posicao, o
/// que sugerir no Retrabalho e a conferencia do que o PCP pediu. A previa e a escrita usam as mesmas funcoes.
///
/// O que um no do Retrabalho ainda "pede" de pronto, antes do proprio pronto, desce de cima para baixo:
/// - a Peca do topo pede o proprio "a iniciar";
/// - um filho pede `(o que falta ao pai, menos o pronto planejado do pai) x QuantidadePorPai`, menos o que ele ja tem
///   fora de "a iniciar" (um pronto de um uso anterior). "O que falta ao pai" e o `FaltaMontar` dele, limitado ao que o
///   pai por sua vez pede: o pai cujo avo ficou pronto e dispensado pela cascata C6 nao pede mais nada dos filhos.
/// E a mesma regra da cascata (C18), que tira, abaixo do pai pronto, o que o filho tem em producao alem do que ele pede:
/// primeiro o "a iniciar" e, o que faltar, o pronto de um uso anterior. Sem pronto de
/// cima, e o `(a iniciar do pai depois do pronto) x QuantidadePorPai` da spec: num pai do Retrabalho `Aberto`, o
/// `FaltaMontar` e o "a iniciar" dele (C17 da spec da Fase 5B-2 poe o pronto no total montado).
/// </summary>
public static class CalculoDoReaproveitamento
{
  /// <summary>
  /// A sobra viva do no de origem de cada no do Retrabalho que tem origem, por posicao (decisao P8 do plano de backend
  /// da Fase 5B-2): aguardando coleta, `SobraDaColeta` de cada passo; aguardando montagem, a
  /// `SobraEmMontagemPorSetor` (C16 da spec da Fase 5C): no filho de pai que recebe em conjunto, primeiro o que nao
  /// fecha conjunto nos Setores com `UtilizaKit`, depois o que passa do que o pai precisa nos Setores sem a marca; nos
  /// outros, o `ExcessoEmMontagem` por todos os Setores. Sempre em ordem de Id do Setor, ate o saldo de cada um. O no
  /// acrescentado a mao, sem origem, nao aparece.
  /// Em ordem de profundidade (de cima para baixo), no e posicao.
  /// </summary>
  public static IReadOnlyList<SobraDisponivel> Disponivel(
      CalculadoraDeExecucao original, IReadOnlyDictionary<int, int> origemDe)
  {
    var idsDoOriginal = original.Nos.Select(n => n.Id).ToHashSet();
    var lista = new List<SobraDisponivel>();
    foreach (var (noDoRetrabalho, noDeOrigem) in origemDe)
    {
      if (!idsDoOriginal.Contains(noDeOrigem)) continue;

      foreach (var (local, _) in original.Saldos(noDeOrigem).Where(s => s.Local.Posicao == Posicoes.AguardandoColeta))
      {
        var sobra = original.SobraDaColeta(noDeOrigem, local.SetorId!.Value, local.Ordem!.Value);
        if (sobra > 0m) lista.Add(new SobraDisponivel(noDoRetrabalho, noDeOrigem, local, sobra));
      }

      foreach (var (setorId, sobra) in original.SobraEmMontagemPorSetor(noDeOrigem))
        lista.Add(new SobraDisponivel(noDoRetrabalho, noDeOrigem, Local.AguardandoMontagem(setorId), sobra));
    }

    return lista
        .OrderBy(s => Profundidade(original, s.NoDeOrigemId))
        .ThenBy(s => s.NoDoRetrabalhoId)
        .ThenBy(s => Livro.OrdemDaPosicao(s.Local.Posicao))
        .ThenBy(s => s.Local.Ordem ?? 0)
        .ThenBy(s => s.Local.SetorId ?? 0)
        .ToList();
  }

  /// <summary>
  /// A sugestao da previa, de cima para baixo (secao 4.4 da spec da Fase 5B-2): em cada posicao, `min(sobra, precisa)`,
  /// e o sugerido de um no reduz o que os filhos dele precisam. `Precisa` e o que o no ainda pode receber de pronto antes
  /// desta posicao. Posicao que nao existe no Roteiro do Retrabalho sugere zero: a confirmacao a recusaria. Na mesma
  /// ordem de `disponivel`.
  /// </summary>
  public static IReadOnlyList<(SobraDisponivel, decimal Precisa, decimal Sugerido)> Sugerir(
      CalculadoraDeExecucao retrabalho, IReadOnlyList<SobraDisponivel> disponivel)
  {
    var plano = new Plano(retrabalho);
    var resultado = new (SobraDisponivel, decimal Precisa, decimal Sugerido)[disponivel.Count];
    foreach (var i in DeCimaParaBaixo(retrabalho, disponivel.Count, i => disponivel[i].NoDoRetrabalhoId, i => disponivel[i].Local))
    {
      var sobra = disponivel[i];
      if (!plano.Existe(sobra.NoDoRetrabalhoId))
      {
        resultado[i] = (sobra, 0m, 0m);
        continue;
      }

      var precisa = plano.Precisa(sobra.NoDoRetrabalhoId);
      var sugerido = PosicaoExiste(retrabalho, sobra.NoDoRetrabalhoId, sobra.Local) ? Math.Min(sobra.Sobra, precisa) : 0m;
      plano.Planejar(sobra.NoDoRetrabalhoId, sugerido);
      resultado[i] = (sobra, precisa, sugerido);
    }
    return resultado;
  }

  /// <summary>
  /// Confere o que o PCP pediu, e o devolve de cima para baixo. A precedencia das recusas e fixa: primeiro o que e erro
  /// do pedido, item a item na ordem recebida (lista vazia, no fora do Retrabalho, quantidade, posicao repetida e posicao
  /// que o pronto nao pode ocupar, 400 `PosicaoDoProntoInexistente`); depois o que depende do saldo, do mais local ao
  /// que depende do resto da arvore: alem da sobra viva do no de origem naquela posicao (409 `AlemDaSobra`, C19 da spec
  /// da Fase 5B-2), alem do "a iniciar" do proprio no (409 `SaldoInsuficiente`), alem do que o pai ainda pede depois do
  /// pronto dele, neste pedido ou num uso anterior (409 `AlemDoQueOPaiPrecisa`), conferido de cima para baixo.
  /// </summary>
  public static Result<IReadOnlyList<ItemReaproveitado>> Conferir(
      CalculadoraDeExecucao retrabalho, IReadOnlyList<SobraDisponivel> disponivel, IReadOnlyList<ItemReaproveitado> pedido,
      Func<int, string> nome)
  {
    if (pedido.Count == 0) return Falhas.LoteVazio<IReadOnlyList<ItemReaproveitado>>();

    var plano = new Plano(retrabalho);
    var vistos = new HashSet<(int, Local)>();
    foreach (var item in pedido)
    {
      if (!plano.Existe(item.NoDoRetrabalhoId)) return Falhas.NaoEncontrado<IReadOnlyList<ItemReaproveitado>>();
      if (item.Quantidade <= 0m || !Quantidades.CabeNaColuna(item.Quantidade))
        return Falhas.QuantidadeInvalida<IReadOnlyList<ItemReaproveitado>>(item.Quantidade);
      if (!vistos.Add((item.NoDoRetrabalhoId, item.Local)))
        return Falhas.Validacao<IReadOnlyList<ItemReaproveitado>>(CodigosDaExecucao.ItemRepetido,
            $"A mesma posição de {nome(item.NoDoRetrabalhoId)} veio duas vezes: junte as quantidades numa linha só.");
      if (RecusaDaPosicao(retrabalho, item, nome) is string recusa)
        return Falhas.Validacao<IReadOnlyList<ItemReaproveitado>>(CodigosDoRetrabalho.PosicaoDoProntoInexistente, recusa);
    }

    foreach (var item in pedido)
    {
      var sobra = disponivel
          .Where(s => s.NoDoRetrabalhoId == item.NoDoRetrabalhoId && s.Local == item.Local)
          .Sum(s => s.Sobra);
      if (item.Quantidade > sobra)
        return Falhas.Conflito<IReadOnlyList<ItemReaproveitado>>(CodigosDoRetrabalho.AlemDaSobra,
            $"Só sobram {Quantidades.Formatar(sobra)} de {nome(item.NoDoRetrabalhoId)} nessa posição no Pedido de origem.");
    }

    var ordem = DeCimaParaBaixo(retrabalho, pedido.Count, i => pedido[i].NoDoRetrabalhoId, i => pedido[i].Local)
        .Select(i => pedido[i]).ToList();

    foreach (var porNo in ordem.GroupBy(i => i.NoDoRetrabalhoId))
    {
      var aIniciar = retrabalho.Saldo(porNo.Key, Local.AIniciar);
      if (porNo.Sum(i => i.Quantidade) > aIniciar)
        return Falhas.Conflito<IReadOnlyList<ItemReaproveitado>>(CodigosDaExecucao.SaldoInsuficiente,
            $"Só há {Quantidades.Formatar(Math.Max(0m, aIniciar))} de {nome(porNo.Key)} a iniciar no Retrabalho.");
    }

    foreach (var porNo in ordem.GroupBy(i => i.NoDoRetrabalhoId))
    {
      var pedidoDoNo = porNo.Sum(i => i.Quantidade);
      if (retrabalho.No(porNo.Key).PaiId is int paiId && pedidoDoNo > plano.PedeDoPai(porNo.Key))
        return Falhas.Conflito<IReadOnlyList<ItemReaproveitado>>(CodigosDaExecucao.AlemDoQueOPaiPrecisa,
            $"Só {Quantidades.Formatar(plano.PedeDoPai(porNo.Key))} de {nome(porNo.Key)} podem ficar prontas: "
            + $"é o que {nome(paiId)} ainda pede depois do pronto dele.");
      plano.Planejar(porNo.Key, pedidoDoNo);
    }

    return Result<IReadOnlyList<ItemReaproveitado>>.Ok(ordem);
  }

  /// <summary>
  /// Por que o pronto nao pode ocupar a posicao, ou nulo (secao 3.1 da spec da Fase 5B-2): o pronto vai para a mesma
  /// posicao fisica da sobra, e ela tem de existir no Retrabalho.
  /// </summary>
  private static string? RecusaDaPosicao(CalculadoraDeExecucao retrabalho, ItemReaproveitado item, Func<int, string> nome)
  {
    if (PosicaoExiste(retrabalho, item.NoDoRetrabalhoId, item.Local)) return null;
    return item.Local.Posicao switch
    {
      Posicoes.AguardandoColeta =>
          $"{nome(item.NoDoRetrabalhoId)} não tem mais esse passo no Roteiro do Retrabalho: a sobra não tem onde ficar pronta.",
      Posicoes.AguardandoMontagem =>
          $"O pai de {nome(item.NoDoRetrabalhoId)} não começa mais nesse Setor no Retrabalho: a sobra não tem onde ficar pronta.",
      _ => $"{nome(item.NoDoRetrabalhoId)} só fica pronto aguardando coleta ou aguardando montagem, como a sobra.",
    };
  }

  /// <summary>
  /// A posicao do pronto existe no Retrabalho: aguardando coleta num passo (Setor e `Ordem`) do Roteiro do no; aguardando
  /// montagem no Setor do primeiro passo do pai, onde o pai comeca consumindo os filhos (como `DestinoDaColeta`).
  /// </summary>
  private static bool PosicaoExiste(CalculadoraDeExecucao retrabalho, int noId, Local local)
  {
    var no = retrabalho.No(noId);
    return local.Posicao switch
    {
      Posicoes.AguardandoColeta => local.SetorId is int setor && local.Ordem is int ordem
          && no.Roteiro.Contains(new PassoDoCalculo(setor, ordem)),
      Posicoes.AguardandoMontagem => local.SetorId is int setor && local.Ordem is null
          && no.PaiId is int paiId && retrabalho.Nos.Any(n => n.Id == paiId)
          && retrabalho.PrimeiroPasso(paiId)?.SetorId == setor,
      _ => false,
    };
  }

  /// <summary>
  /// Os indices em ordem de profundidade no Retrabalho (o pai antes do filho), depois no e posicao: e o que faz o pronto
  /// planejado de um pai chegar antes de o filho ser conferido. No fora do Retrabalho vai ao fim.
  /// </summary>
  private static IEnumerable<int> DeCimaParaBaixo(
      CalculadoraDeExecucao retrabalho, int quantos, Func<int, int> noDe, Func<int, Local> localDe)
  {
    var ids = retrabalho.Nos.Select(n => n.Id).ToHashSet();
    return Enumerable.Range(0, quantos)
        .OrderBy(i => ids.Contains(noDe(i)) ? Profundidade(retrabalho, noDe(i)) : int.MaxValue)
        .ThenBy(noDe)
        .ThenBy(i => Livro.OrdemDaPosicao(localDe(i).Posicao))
        .ThenBy(i => localDe(i).Ordem ?? 0)
        .ThenBy(i => localDe(i).SetorId ?? 0);
  }

  private static int Profundidade(CalculadoraDeExecucao calc, int id)
  {
    var profundidade = 0;
    var ids = calc.Nos.Select(n => n.Id).ToHashSet();
    for (var no = calc.No(id); no.PaiId is int paiId && ids.Contains(paiId); no = calc.No(paiId)) profundidade++;
    return profundidade;
  }

  /// <summary>
  /// O livro guarda quatro casas: a razao com casas, vezes o que falta, pode dar uma quinta. O que o no pode receber
  /// arredonda para baixo, para nunca passar do que o pai pede.
  /// </summary>
  private static decimal QuatroCasasParaBaixo(decimal quantidade) => decimal.Floor(quantidade * 10_000m) / 10_000m;

  /// <summary>
  /// O pronto sendo planejado sobre o estado de antes, e o que cada no ainda pode receber dele. "Pede" e calculado
  /// subindo ate a Peca do topo a cada consulta: a arvore e pequena e o planejado muda entre uma consulta e outra.
  /// </summary>
  private sealed class Plano
  {
    private readonly CalculadoraDeExecucao _calc;
    private readonly HashSet<int> _ids;
    private readonly Dictionary<int, decimal> _planejado = new();

    public Plano(CalculadoraDeExecucao calc)
    {
      _calc = calc;
      _ids = calc.Nos.Select(n => n.Id).ToHashSet();
    }

    public bool Existe(int id) => _ids.Contains(id);

    public void Planejar(int id, decimal quantidade) => _planejado[id] = Planejado(id) + quantidade;

    private decimal Planejado(int id) => _planejado.GetValueOrDefault(id);

    private decimal AIniciar(int id) => _calc.Saldo(id, Local.AIniciar);

    /// <summary>O que o no ja tem em producao fora de "a iniciar": o pronto de um uso anterior.</summary>
    private decimal ForaDeAIniciar(int id) => _calc.EmProducao(id) - AIniciar(id);

    /// <summary>
    /// O que o pai ainda pede deste filho, antes do pronto do proprio filho neste plano:
    /// `max(0, (FaltaPai - pronto planejado do pai) x razao - o que o filho ja tem fora de "a iniciar")`. Na Peca do
    /// topo, sem pai, o "a iniciar" dela.
    /// </summary>
    public decimal PedeDoPai(int id)
    {
      var no = _calc.No(id);
      if (no.PaiId is not int paiId || !_ids.Contains(paiId)) return Math.Max(0m, AIniciar(id));
      var doPai = Math.Max(0m, Falta(paiId) - Planejado(paiId));
      return QuatroCasasParaBaixo(Math.Max(0m, doPai * (no.QuantidadePorPai ?? 0m) - ForaDeAIniciar(id)));
    }

    /// <summary>
    /// Quanto do no ainda falta, antes do pronto planejado dele: na Peca do topo, o `FaltaMontar` (ou o "a iniciar",
    /// numa Peca sem filhos); abaixo, o `FaltaMontar` limitado ao que o pai pede dele (a cascata C6).
    /// </summary>
    private decimal Falta(int id)
    {
      var no = _calc.No(id);
      var propria = _calc.TemFilhos(id) ? Math.Max(0m, _calc.FaltaMontar(id)) : Math.Max(0m, AIniciar(id));
      return no.PaiId is int paiId && _ids.Contains(paiId) ? Math.Min(propria, PedeDoPai(id)) : propria;
    }

    /// <summary>O que o no ainda pode receber de pronto: o menor entre o "a iniciar" e o que o pai pede, menos o planejado.</summary>
    public decimal Precisa(int id) =>
        Math.Max(0m, Math.Min(AIniciar(id), PedeDoPai(id)) - Planejado(id));
  }
}

using Rastreamento.Application.Baixa;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Retrabalho;

/// <summary>
/// "Usar a sobra" (secao 4.4 da spec da Fase 5B-2; D2, C4, C11, C12, C18, C19): a sobra viva da arvore da Peca de
/// origem, no Pedido original, vira pronto no Retrabalho `Aberto`, na mesma posicao fisica (secao 3.1). Uma transacao
/// so: no original, uma baixa `Reaproveitado` apontando o Retrabalho, com uma linha `Perda` por no e posicao; no
/// Retrabalho, um `Pronto` por linha, apontando aquela baixa; e, se o pronto de um pai dispensa filhos "a iniciar", a
/// cascata C6 numa baixa de `Descarte` a parte, no Retrabalho, ligada a do original (decisao P2 do plano de backend da
/// Fase 5B-2). A conta e `CalculoDoReaproveitamento` (a previa e a escrita usam a mesma) e `CalculoDaBaixa`
/// (a cascata). O estorno e o da baixa `Reaproveitado`, em `BaixaUseCase`.
///
/// A concorrencia (decisao P6 do plano de backend da Fase 5B-2): trava, em ordem crescente de Id, a uniao dos nos da
/// arvore da Peca de origem e dos nos do Retrabalho, e depois os dois Pedidos, em ordem de Id (spec da Fase 3, secao
/// 8.1). O descarte da mesma sobra no original trava os mesmos nos do original e espera: a escrita que chega depois ve
/// o saldo da primeira.
/// </summary>
public sealed class ReaproveitamentoUseCase
{
  private const string StatusAberto = "Aberto";
  private const string StatusCancelado = "Cancelado";
  private const string TipoRetrabalho = "Retrabalho";

  private readonly IExecucaoRepository _execucao;
  private readonly IBaixaRepository _baixas;
  private readonly IPedidoRepository _pedidos;
  private readonly IReceitaPadraoRepository _catalogo;
  private readonly BaixaUseCase _baixa;
  private readonly LeitorDeEstado _leitor;
  private readonly FechamentoDoPedido _fechamento;

  public ReaproveitamentoUseCase(
      IExecucaoRepository execucao, IBaixaRepository baixas, IPedidoRepository pedidos, IEstruturaRepository estruturas,
      IReceitaPadraoRepository catalogo, BaixaUseCase baixa)
  {
    _execucao = execucao;
    _baixas = baixas;
    _pedidos = pedidos;
    _catalogo = catalogo;
    _baixa = baixa;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _fechamento = new FechamentoDoPedido(execucao);
  }

  /// <summary>O Retrabalho, o Pedido de origem e os nos dos dois, lidos fora da transacao: o que se trava.</summary>
  private sealed record Escopo(
      int RetrabalhoId, int OriginalId, IReadOnlyList<int> NosDoRetrabalho, IReadOnlyList<int> NosDoOriginal,
      PedidoTravado Retrabalho, PedidoTravado Original);

  /// <summary>O estado dos dois Pedidos, depois das travas (ou sem elas, na previa).</summary>
  private sealed record Estados(
      EstadoDeExecucao Original, EstadoDeExecucao Retrabalho, IReadOnlyList<EstruturaItem> NosDoOriginal,
      IReadOnlyList<EstruturaItem> NosDoRetrabalho, IReadOnlyDictionary<int, int> OrigemDe);

  /// <summary>
  /// A previa (C14 da spec da Fase 5B-1, no molde): a sobra viva do original por no do Retrabalho e posicao, com o que
  /// sugerir, sem travar nem gravar. As mesmas recusas de Pedido da escrita.
  /// </summary>
  public async Task<Result<PreviaDoReaproveitamentoDto>> Prever(int retrabalhoId, CancellationToken ct) =>
      await _execucao.ConsultarAsync(async () =>
      {
        var escopo = await LerEscopoAsync(retrabalhoId, ct);
        if (!escopo.Sucesso) return Falhas.Repassar<PreviaDoReaproveitamentoDto, Escopo>(escopo);
        var (_, _, nosDoRetrabalho, nosDoOriginal, retrabalho, original) = escopo.Valor!;

        var recusa = RecusaDosPedidos(retrabalho, original);
        if (recusa is not null) return Falhas.Repassar<PreviaDoReaproveitamentoDto, Escopo>(recusa);

        var estados = await CarregarAsync(
            await _execucao.ListarNosAsync(nosDoOriginal, ct), await _execucao.ListarNosAsync(nosDoRetrabalho, ct), ct);
        var disponivel = CalculoDoReaproveitamento.Disponivel(estados.Original.Calc, estados.OrigemDe);
        var sugestao = CalculoDoReaproveitamento.Sugerir(estados.Retrabalho.Calc, disponivel);
        var setores = await NomesDosSetoresAsync(disponivel.Select(s => s.Local.SetorId).OfType<int>(), ct);

        return Result<PreviaDoReaproveitamentoDto>.Ok(new PreviaDoReaproveitamentoDto(sugestao.Select(s =>
        {
          var (sobra, precisa, sugerido) = s;
          var no = sobra.NoDoRetrabalhoId;
          return new SobraReaproveitavelDto(
              no, estados.Retrabalho.Nome(no), estados.Retrabalho.Codigos.GetValueOrDefault(no), sobra.Local.Posicao,
              sobra.Local.SetorId, sobra.Local.SetorId is int setor ? setores.GetValueOrDefault(setor) : null,
              sobra.Local.Ordem, sobra.Sobra, precisa, sugerido);
        }).ToList()));
      }, ct);

  /// <summary>
  /// Usa a sobra. Antes da transacao, as recusas do corpo (lista, teto do lote, posicao conhecida) e o escopo (404 se
  /// o Pedido nao e um Retrabalho). Dentro dela, nesta ordem: as travas (decisao P6), o Retrabalho fora de `Aberto`
  /// (409 `RetrabalhoForaDeAberto`, C12), o original `Cancelado` (409 `PedidoFechado`; o `Concluido` aceita, C9) e a
  /// conferencia (`CalculoDoReaproveitamento.Conferir`). `201` com a baixa `Reaproveitado`, projetada como as outras.
  /// </summary>
  public async Task<Result<BaixaDto>> Reaproveitar(
      int retrabalhoId, NovoReaproveitamentoDto dto, int usuarioId, CancellationToken ct)
  {
    var validado = Validar(dto);
    if (!validado.Sucesso) return Falhas.Repassar<BaixaDto, IReadOnlyList<ItemReaproveitado>>(validado);
    var pedido = validado.Valor!;

    // Lido FORA da transacao, como na abertura do Retrabalho: lido dentro, sob SERIALIZABLE, o escopo deixaria travas
    // compartilhadas no Pedido e na arvore enquanto a escrita espera os nos, e a outra escrita, ao gravar o fechamento,
    // cairia nelas (deadlock). O que muda entre a leitura e as travas e relido depois delas.
    var lido = await _execucao.ConsultarAsync(() => LerEscopoAsync(retrabalhoId, ct), ct);
    if (!lido.Sucesso) return Falhas.Repassar<BaixaDto, Escopo>(lido);
    var escopo = lido.Valor!;

    return await _execucao.ExecutarAsync(async () =>
    {
      var travados = (await _execucao.TravarNosAsync(escopo.NosDoOriginal.Union(escopo.NosDoRetrabalho), ct)).ToList();
      // Relido depois das travas: o no acrescentado a mao ao Retrabalho entre a leitura e a trava entra travado tambem.
      var nosDoRetrabalho = await NosDosPedidosAsync([retrabalhoId], ct);
      var acrescentados = nosDoRetrabalho.Except(travados.Select(n => n.Id)).ToList();
      if (acrescentados.Count > 0) travados.AddRange(await _execucao.TravarNosAsync(acrescentados, ct));
      var pedidosTravados = await _execucao.TravarPedidosDosNosAsync(travados.Select(n => n.Id).ToList(), ct);

      var retrabalho = await _execucao.TravarPedidoAsync(retrabalhoId, ct);
      var original = await _execucao.TravarPedidoAsync(escopo.OriginalId, ct);
      if (retrabalho is null || original is null) return Falhas.NaoEncontrado<BaixaDto>();
      var recusa = RecusaDosPedidos(retrabalho, original);
      if (recusa is not null) return Falhas.Repassar<BaixaDto, Escopo>(recusa);

      var doOriginal = travados.Where(n => escopo.NosDoOriginal.Contains(n.Id)).ToList();
      var doRetrabalho = travados.Where(n => nosDoRetrabalho.Contains(n.Id)).ToList();
      var estados = await CarregarAsync(doOriginal, doRetrabalho, ct);
      var disponivel = CalculoDoReaproveitamento.Disponivel(estados.Original.Calc, estados.OrigemDe);
      var conferido = CalculoDoReaproveitamento.Conferir(estados.Retrabalho.Calc, disponivel, pedido, estados.Retrabalho.Nome);
      if (!conferido.Sucesso) return Falhas.Repassar<BaixaDto, IReadOnlyList<ItemReaproveitado>>(conferido);
      var itens = conferido.Valor!;

      var agora = DateTime.UtcNow;
      var perda = await GravarReaproveitadoAsync(escopo, estados, itens, usuarioId, agora, ct);
      await GravarDispensaDoProntoAsync(retrabalhoId, perda.Id, doRetrabalho, itens, usuarioId, agora, ct);
      await _fechamento.RecalcularAsync(pedidosTravados, agora, ct);

      return Result<BaixaDto>.Ok((await _baixa.ProjetarAsync([perda], new QuemLe(usuarioId, false), ct)).Single());
    }, ct);
  }

  /// <summary>
  /// A baixa `Reaproveitado` no original (C4: `PedidoRetrabalhoId` = o Retrabalho; o escopo e a Peca de origem) com uma
  /// linha `Perda` por no de origem e posicao, e um `Pronto` por linha no Retrabalho, na mesma posicao, apontando a
  /// baixa. Duas gravacoes, como a baixa: a primeira da o Id que as linhas apontam.
  /// </summary>
  private async Task<Perda> GravarReaproveitadoAsync(
      Escopo escopo, Estados estados, IReadOnlyList<ItemReaproveitado> itens, int usuarioId, DateTime agora,
      CancellationToken ct)
  {
    var primeiroDeOrigem = estados.OrigemDe[itens[0].NoDoRetrabalhoId];
    var perda = new Perda
    {
      Motivo = MotivosDePerda.Reaproveitado, PedidoId = escopo.OriginalId,
      EstruturaItemId = Raiz(estados.NosDoOriginal, primeiroDeOrigem), Tudo = false,
      PedidoRetrabalhoId = escopo.RetrabalhoId, DataHora = agora, UsuarioId = usuarioId,
    };
    _baixas.Adicionar(perda);
    await _execucao.SalvarAlteracoesAsync(ct);

    foreach (var item in itens)
    {
      var daSobra = NovoMovimento.De(
          estados.OrigemDe[item.NoDoRetrabalhoId], TiposDeMovimentacao.Perda, item.Quantidade, item.Local, Local.Perdido,
          usuarioId);
      var pronto = NovoMovimento.De(
          item.NoDoRetrabalhoId, TiposDeMovimentacao.Pronto, item.Quantidade, Local.AIniciar, item.Local, usuarioId);
      foreach (var movimento in new[] { daSobra, pronto })
      {
        movimento.PerdaId = perda.Id;
        movimento.DataHora = agora;
        _execucao.Adicionar(movimento);
      }
    }
    await _execucao.SalvarAlteracoesAsync(ct);
    return perda;
  }

  /// <summary>
  /// Os filhos "a iniciar" que o pronto dispensa (C18), sobre o estado do Retrabalho RELIDO depois dos `Pronto`: e so
  /// entao que o pronto de um pai soma ao total montado dele (C17). Sem linha, nada se grava. Com linhas, uma baixa de
  /// `Descarte` no Retrabalho, sem "tudo" (o no nao sai do projeto, C22 da spec da Fase 5B-1), ligada a
  /// `Reaproveitado` por `PerdaDeOrigemId` (decisao P2 do plano de backend da Fase 5B-2).
  /// </summary>
  private async Task GravarDispensaDoProntoAsync(
      int retrabalhoId, int perdaDeOrigemId, IReadOnlyList<EstruturaItem> nosDoRetrabalho,
      IReadOnlyList<ItemReaproveitado> itens, int usuarioId, DateTime agora, CancellationToken ct)
  {
    var depois = await _leitor.CarregarAsync(nosDoRetrabalho, ct);
    var linhas = CalculoDaBaixa.PlanejarDispensaDoPronto(
        depois.Calc, itens.Select(i => i.NoDoRetrabalhoId).Distinct().ToList());
    if (linhas.Count == 0) return;

    var ligada = new Perda
    {
      Motivo = MotivosDePerda.Descarte, PedidoId = retrabalhoId, EstruturaItemId = Raiz(nosDoRetrabalho, linhas[0].NoId),
      Tudo = false, PerdaDeOrigemId = perdaDeOrigemId, DataHora = agora, UsuarioId = usuarioId,
    };
    _baixas.Adicionar(ligada);
    await _execucao.SalvarAlteracoesAsync(ct);
    foreach (var linha in linhas)
    {
      var movimento = NovoMovimento.De(
          linha.NoId, TiposDeMovimentacao.Perda, linha.Quantidade, linha.Origem, Local.Perdido, usuarioId);
      movimento.PerdaId = ligada.Id;
      movimento.DataHora = agora;
      _execucao.Adicionar(movimento);
    }
    await _execucao.SalvarAlteracoesAsync(ct);
  }

  /// <summary>
  /// O Pedido tem de ser um Retrabalho (404, como o de um Pedido que nao existe: a sobra reaproveitavel e so dele). Os
  /// nos do Retrabalho sao as arvores das Pecas dele; os do original, as arvores das Pecas de que as do Retrabalho
  /// vieram (`OrigemEstruturaItemId` da Peca copiada, C14).
  /// </summary>
  private async Task<Result<Escopo>> LerEscopoAsync(int retrabalhoId, CancellationToken ct)
  {
    var retrabalho = await _pedidos.ObterPorIdAsync(retrabalhoId, ct);
    if (retrabalho is not { Tipo: TipoRetrabalho, PedidoOrigemId: int originalId }) return Falhas.NaoEncontrado<Escopo>();
    var original = await _pedidos.ObterPorIdAsync(originalId, ct);
    if (original is null) return Falhas.NaoEncontrado<Escopo>();

    var pecas = (await _execucao.ListarPecasDosPedidosAsync([retrabalhoId], ct)).Select(p => p.PecaId).ToList();
    var nosDoRetrabalho = await SubarvoresAsync(pecas, ct);
    var pecasDeOrigem = (await _execucao.ListarNosAsync(pecas, ct))
        .Select(n => n.OrigemEstruturaItemId).OfType<int>().Distinct().ToList();
    var nosDoOriginal = await SubarvoresAsync(pecasDeOrigem, ct);
    return Result<Escopo>.Ok(new Escopo(retrabalhoId, originalId, nosDoRetrabalho, nosDoOriginal,
        new PedidoTravado(retrabalho.Id, retrabalho.Numero, retrabalho.Status),
        new PedidoTravado(original.Id, original.Numero, original.Status)));
  }

  private async Task<IReadOnlyList<int>> NosDosPedidosAsync(IReadOnlyCollection<int> pedidoIds, CancellationToken ct) =>
      await SubarvoresAsync((await _execucao.ListarPecasDosPedidosAsync(pedidoIds, ct)).Select(p => p.PecaId).ToList(), ct);

  private async Task<IReadOnlyList<int>> SubarvoresAsync(IReadOnlyList<int> raizes, CancellationToken ct)
  {
    var ids = new HashSet<int>();
    foreach (var raiz in raizes) ids.UnionWith(await _execucao.ListarIdsDaSubarvoreAsync(raiz, ct));
    return ids.Order().ToList();
  }

  private async Task<Estados> CarregarAsync(
      IReadOnlyList<EstruturaItem> nosDoOriginal, IReadOnlyList<EstruturaItem> nosDoRetrabalho, CancellationToken ct)
  {
    var original = await _leitor.CarregarAsync(nosDoOriginal, ct);
    var retrabalho = await _leitor.CarregarAsync(nosDoRetrabalho, ct);
    var origemDe = nosDoRetrabalho
        .Where(n => n.OrigemEstruturaItemId is not null)
        .ToDictionary(n => n.Id, n => n.OrigemEstruturaItemId!.Value);
    return new Estados(original, retrabalho, nosDoOriginal, nosDoRetrabalho, origemDe);
  }

  /// <summary>A Peca do topo do no, subindo pelos nos lidos.</summary>
  private static int Raiz(IReadOnlyList<EstruturaItem> nos, int id)
  {
    var porId = nos.ToDictionary(n => n.Id);
    var atual = id;
    while (porId.TryGetValue(atual, out var no) && no.EstruturaPaiId is int pai && porId.ContainsKey(pai)) atual = pai;
    return atual;
  }

  /// <summary>
  /// O Retrabalho so recebe pronto antes do primeiro inicio (C11, C12: 409 `RetrabalhoForaDeAberto`); o original
  /// `Cancelado` nao da baixa (409 `PedidoFechado`), e o `Concluido` aceita, porque a sobra e de Item (C9).
  /// </summary>
  private static Result<Escopo>? RecusaDosPedidos(PedidoTravado retrabalho, PedidoTravado original)
  {
    if (retrabalho.Status != StatusAberto)
      return Falhas.Conflito<Escopo>(CodigosDoRetrabalho.RetrabalhoForaDeAberto,
          $"O Retrabalho {retrabalho.Numero} já começou: a sobra só vira pronto antes do primeiro início.");
    if (original.Status == StatusCancelado)
      return Falhas.Conflito<Escopo>(CodigosDaExecucao.PedidoFechado,
          $"O Pedido {original.Numero} foi cancelado: a sobra dele não se reaproveita.");
    return null;
  }

  /// <summary>
  /// As recusas do corpo que nao dependem do estado: a lista vazia, o teto do lote e a posicao conhecida (400
  /// `PosicaoDoProntoInexistente`). A quantidade, a posicao repetida e a que o pronto nao pode ocupar sao da conta.
  /// </summary>
  private static Result<IReadOnlyList<ItemReaproveitado>> Validar(NovoReaproveitamentoDto dto)
  {
    var itens = dto.Itens ?? [];
    if (itens.Count == 0) return Falhas.LoteVazio<IReadOnlyList<ItemReaproveitado>>();
    if (itens.Count > ApontamentoUseCase.TamanhoMaximoDoLote) return Falhas.LoteGrandeDemais<IReadOnlyList<ItemReaproveitado>>();

    var lista = new List<ItemReaproveitado>(itens.Count);
    foreach (var item in itens)
    {
      if (item.Posicao is not { } posicao || !Posicoes.Todas.Contains(posicao))
        return Falhas.Validacao<IReadOnlyList<ItemReaproveitado>>(CodigosDoRetrabalho.PosicaoDoProntoInexistente,
            "Diga em que posição está a sobra de cada linha.");
      lista.Add(new ItemReaproveitado(item.EstruturaItemId, new Local(posicao, item.SetorId, item.Ordem), item.Quantidade));
    }
    return Result<IReadOnlyList<ItemReaproveitado>>.Ok(lista);
  }

  private async Task<IReadOnlyDictionary<int, string>> NomesDosSetoresAsync(IEnumerable<int> ids, CancellationToken ct)
  {
    var distintos = ids.Distinct().ToList();
    return distintos.Count == 0
        ? new Dictionary<int, string>()
        : (await _catalogo.ObterSetoresPorIdAsync(distintos, ct)).ToDictionary(s => s.Id, s => s.Nome);
  }
}

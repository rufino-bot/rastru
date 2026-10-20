using Rastreamento.Application.Common;
using Rastreamento.Application.Expedicao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// O Movimentador entrega uma LISTA, numa transacao so: ou tudo grava, ou nada (spec da Fase 3, secao
/// 4.3). Filhos de Kit que vao a montagem num Setor com `UtilizaKit` entram em conjunto completo
/// (`ConjuntoCompleto`). Um mesmo no pode aparecer mais de uma vez: cada item desconta do saldo que os
/// anteriores ja consumiram.
/// </summary>
public sealed class EntregaUseCase
{
  private sealed record Recusa(string Codigo, TipoDeErro Tipo, string Mensagem);

  private readonly IExecucaoRepository _execucao;
  private readonly IReceitaPadraoRepository _catalogo;
  private readonly LeitorDeEstado _leitor;
  private readonly ProjetorDoLivro _projetor;
  private readonly FechamentoDoPedido _fechamento;

  public EntregaUseCase(IExecucaoRepository execucao, IEstruturaRepository estruturas, IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _catalogo = catalogo;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _projetor = new ProjetorDoLivro(execucao, catalogo);
    _fechamento = new FechamentoDoPedido(execucao);
  }

  public async Task<Result<IReadOnlyList<MovimentacaoDto>>> Entregar(EntregaDto dto, int usuarioId, CancellationToken ct)
  {
    var itens = dto.Itens ?? Array.Empty<ItemDaEntregaDto>();
    if (itens.Count == 0)
      return Falhas.Validacao<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.EntregaVazia,
          "Escolha pelo menos um item para entregar.");

    var origens = new List<Local>();
    foreach (var item in itens)
    {
      if (!Quantidades.CabeNaColuna(item.Quantidade))
        return Falhas.QuantidadeInvalida<IReadOnlyList<MovimentacaoDto>>(item.Quantidade);
      if (OrigemValida(item.Origem) is not Local origem)
        return Falhas.Validacao<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.OrigemInvalida,
            "A origem de uma entrega é o que aguarda coleta (com Setor e passo) ou o que aguarda montagem, "
            + "aguarda conserto ou está pronto do conserto (com Setor, sem passo).");
      origens.Add(origem);
    }

    return await _execucao.ExecutarAsync(async () =>
    {
      var ids = itens.Select(i => i.EstruturaItemId).Distinct().ToList();
      var travados = await _execucao.TravarNosAsync(ids, ct);
      if (travados.Count != ids.Count) return Falhas.NaoEncontrado<IReadOnlyList<MovimentacaoDto>>();
      // So a Peca chega ao local de expedicao, entao so ela pode mudar o fechamento: o Pedido dela e travado
      // depois dos nos (spec da Fase 3, secao 8.1) e antes da primeira leitura dele (decisao P4 do plano de
      // backend da Fase 5A). Entrega so de Item nao trava Pedido nenhum.
      var pecas = travados.Where(n => n.EstruturaPaiId is null).Select(n => n.Id).ToList();
      var pedidosTravados = pecas.Count > 0 ? await _execucao.TravarPedidosDosNosAsync(pecas, ct) : [];
      foreach (var id in ids)
        if (Falhas.EstaFechado(await _execucao.ObterPedidoDoNoAsync(id, ct)))
          return Falhas.PedidoFechado<IReadOnlyList<MovimentacaoDto>>();

      // Os pais e os irmaos entram no estado, mas nao sao travados: a entrega so le o Roteiro dos pais e, para o
      // conjunto completo, os filhos de cada pai (spec da Fase 3B, secao 4.3). So os nos da lista sao escritos, e
      // o conjunto completo obriga a lista a trazer todos os filhos: duas entregas do mesmo Kit travam os mesmos nos.
      var paiIds = travados.Where(n => n.EstruturaPaiId is not null).Select(n => n.EstruturaPaiId!.Value).Distinct().ToList();
      var pais = await _execucao.ListarNosAsync(paiIds.Except(ids).ToList(), ct);
      var irmaos = new List<EstruturaItem>();
      foreach (var paiId in paiIds) irmaos.AddRange(await _execucao.ListarFilhosAsync(paiId, ct));
      var estado = await _leitor.CarregarAsync([.. travados, .. pais, .. irmaos], ct);

      var setorIds = origens.Select(o => o.SetorId!.Value)
          .Concat(travados.Where(n => n.EstruturaPaiId is not null)
              .Select(n => estado.Calc.PrimeiroPasso(n.EstruturaPaiId!.Value)?.SetorId)
              .OfType<int>())
          .Distinct().ToList();
      var nomes = (await _catalogo.ObterSetoresPorIdAsync(setorIds, ct)).ToDictionary(s => s.Id, s => s.Nome);
      string NomeDoSetor(int id) => nomes.TryGetValue(id, out var nome) ? nome : $"Setor {id}";

      var consumido = new Dictionary<(int Item, Local Local), decimal>();
      var movimentos = new List<Movimentacao>();
      var destinados = new List<ConjuntoCompleto.ItemDestinado>();
      for (var i = 0; i < itens.Count; i++)
      {
        var item = itens[i];
        var origem = origens[i];
        var chave = (item.EstruturaItemId, origem);
        var disponivel = estado.Calc.Saldo(item.EstruturaItemId, origem) - consumido.GetValueOrDefault(chave);
        if (item.Quantidade > disponivel)
          return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.SaldoInsuficiente,
              $"Só há {Quantidades.Formatar(Math.Max(0m, disponivel))} de {estado.Nome(item.EstruturaItemId)} "
              + $"{Onde(origem, NomeDoSetor)}.");

        var (destino, recusa) = Destino(estado, item, origem, NomeDoSetor);
        if (recusa is not null)
          return Result<IReadOnlyList<MovimentacaoDto>>.Falha(recusa.Codigo, recusa.Tipo, recusa.Mensagem);

        consumido[chave] = consumido.GetValueOrDefault(chave) + item.Quantidade;
        destinados.Add(new ConjuntoCompleto.ItemDestinado(item.EstruturaItemId, origem, destino!.Value, item.Quantidade));
        movimentos.Add(NovoMovimento.De(
            item.EstruturaItemId, TiposDeMovimentacao.Entrega, item.Quantidade, origem, destino!.Value, usuarioId));
      }

      if (ConjuntoCompleto.Conferir(estado, destinados) is { } recusaDoConjunto)
        return Result<IReadOnlyList<MovimentacaoDto>>.Falha(recusaDoConjunto.Codigo, recusaDoConjunto.Tipo, recusaDoConjunto.Mensagem);

      foreach (var movimento in movimentos) _execucao.Adicionar(movimento);
      await _execucao.SalvarAlteracoesAsync(ct);
      if (movimentos.Any(Posicoes.TocaOFimDoCaminho))
        await _fechamento.RecalcularAsync(pedidosTravados, DateTime.UtcNow, ct);
      return Result<IReadOnlyList<MovimentacaoDto>>.Ok(await _projetor.ProjetarMovimentacoesAsync(movimentos, ct));
    }, ct);
  }

  private static Local? OrigemValida(OrigemDaEntregaDto? origem) => origem switch
  {
    { Posicao: Posicoes.AguardandoColeta, SetorId: int s, Ordem: int k } => Local.AguardandoColeta(s, k),
    { Posicao: Posicoes.AguardandoMontagem, SetorId: int s, Ordem: null } => Local.AguardandoMontagem(s),
    { Posicao: Posicoes.AguardandoConserto, SetorId: int s, Ordem: null } => Local.AguardandoConserto(s),
    { Posicao: Posicoes.ConsertoPronto, SetorId: int s, Ordem: null } => Local.ConsertoPronto(s),
    _ => null,
  };

  private static string Onde(Local origem, Func<int, string> nomeDoSetor)
  {
    var setor = nomeDoSetor(origem.SetorId!.Value);
    return origem.Posicao switch
    {
      Posicoes.AguardandoColeta => $"aguardando coleta no Setor {setor} (passo {origem.Ordem})",
      Posicoes.AguardandoConserto => $"aguardando conserto no Setor {setor}",
      Posicoes.ConsertoPronto => $"pronto do conserto no Setor {setor}",
      _ => $"aguardando montagem no Setor {setor}",
    };
  }

  /// <summary>A tabela da spec secao 4.3, linha a linha.</summary>
  private static (Local? Destino, Recusa? Recusa) Destino(
      EstadoDeExecucao estado, ItemDaEntregaDto item, Local origem, Func<int, string> nomeDoSetor)
  {
    var id = item.EstruturaItemId;
    var nome = estado.Nome(id);

    // O conserto vai e volta sem escolha (D4 da spec da Fase 5A): ao `EmConserto` do mesmo Setor, e ao local de
    // expedicao. O destino mandado e recusado alto, como nas outras origens.
    if (origem.Posicao is Posicoes.AguardandoConserto or Posicoes.ConsertoPronto)
    {
      var ida = origem.Posicao == Posicoes.AguardandoConserto;
      if (item.DestinoSetorId is not null)
        return (null, Indevido(ida
            ? $"{nome} vai para o conserto em {nomeDoSetor(origem.SetorId!.Value)}; esse destino não se escolhe."
            : $"{nome} volta do conserto para o local de expedição; esse destino não se escolhe."));
      return (ida ? Local.EmConserto(origem.SetorId!.Value) : Local.NaExpedicao, null);
    }

    if (origem.Posicao == Posicoes.AguardandoColeta)
    {
      var calculado = estado.Calc.DestinoDaColeta(id, origem.Ordem!.Value);
      switch (calculado.Tipo)
      {
        case TipoDeDestino.ProximoPasso:
          if (item.DestinoSetorId is not null)
            return (null, Indevido($"{nome} vai para o próximo passo do Roteiro; esse destino não se escolhe."));
          return (Local.NoSetor(calculado.Passo!.Value.SetorId, calculado.Passo.Value.Ordem), null);
        case TipoDeDestino.Expedicao:
          if (item.DestinoSetorId is not null)
            return (null, Indevido($"{nome} terminou o Roteiro e vai para o local de expedição; esse destino não se escolhe."));
          return (Local.NaExpedicao, null);
        default:
          return ParaAMontagem(estado, item, calculado.PaiId!.Value, origemEmMontagem: null, nomeDoSetor);
      }
    }

    // Redirecionamento: o que aguarda montagem fora do primeiro passo do pai vai para ele.
    if (estado.Calc.No(id).PaiId is not int paiId)
      return (null, new Recusa(CodigosDaExecucao.OrigemInvalida, TipoDeErro.Validacao,
          $"{nome} é uma Peça: não aguarda montagem de ninguém."));
    return ParaAMontagem(estado, item, paiId, origem, nomeDoSetor);
  }

  /// <summary>
  /// O destino da montagem e o primeiro passo do pai, sem escolha (spec da Fase 3D, secao 2.2). A
  /// ordem das checagens e fixa: pai sem Roteiro sai primeiro, porque e a recusa que diz o que fazer;
  /// depois o destino mandado (o contrato o aceita so para recusa-lo alto, em vez de o JSON o ignorar
  /// em silencio — desvio D2 do plano da Fase 3D); por ultimo o redirecionamento que nao sairia do lugar.
  /// </summary>
  private static (Local? Destino, Recusa? Recusa) ParaAMontagem(
      EstadoDeExecucao estado, ItemDaEntregaDto item, int paiId, Local? origemEmMontagem, Func<int, string> nomeDoSetor)
  {
    var pai = estado.Nome(paiId);
    if (estado.Calc.PrimeiroPasso(paiId) is not PassoDoCalculo primeiro)
      return (null, new Recusa(CodigosDaExecucao.PaiSemRoteiro, TipoDeErro.Conflito,
          $"{pai} não tem Roteiro: o PCP precisa defini-lo antes de receber os filhos."));

    if (item.DestinoSetorId is not null)
      return (null, Indevido(
          $"{estado.Nome(item.EstruturaItemId)} vai para {nomeDoSetor(primeiro.SetorId)}, o primeiro passo de {pai}; esse destino não se escolhe."));

    if (origemEmMontagem?.SetorId == primeiro.SetorId)
      return (null, new Recusa(CodigosDaExecucao.RedirecionamentoSemEfeito, TipoDeErro.Conflito,
          $"{estado.Nome(item.EstruturaItemId)} já aguarda montagem em {nomeDoSetor(primeiro.SetorId)}, o primeiro passo de {pai}."));

    return (Local.AguardandoMontagem(primeiro.SetorId), null);
  }

  private static Recusa Indevido(string mensagem) =>
      new(CodigosDaExecucao.DestinoIndevido, TipoDeErro.Validacao, mensagem);
}

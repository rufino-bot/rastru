using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// O Movimentador entrega uma LISTA, numa transacao so: ou tudo grava, ou nada (spec da Fase 3, secao
/// 4.3). A lista ja serve a 3B, cujo conjunto completo exige que os filhos entrem juntos. Um mesmo no
/// pode aparecer mais de uma vez: cada item desconta do saldo que os anteriores ja consumiram.
/// </summary>
public sealed class EntregaUseCase
{
  private sealed record Recusa(string Codigo, TipoDeErro Tipo, string Mensagem);

  private readonly IExecucaoRepository _execucao;
  private readonly IReceitaPadraoRepository _catalogo;
  private readonly LeitorDeEstado _leitor;
  private readonly ProjetorDoLivro _projetor;

  public EntregaUseCase(IExecucaoRepository execucao, IEstruturaRepository estruturas, IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _catalogo = catalogo;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _projetor = new ProjetorDoLivro(execucao, catalogo);
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
            "A origem de uma entrega é o que aguarda coleta (com Setor e passo) ou o que aguarda montagem "
            + "(com Setor, sem passo).");
      origens.Add(origem);
    }

    return await _execucao.ExecutarAsync(async () =>
    {
      var ids = itens.Select(i => i.EstruturaItemId).Distinct().ToList();
      var travados = await _execucao.TravarNosAsync(ids, ct);
      if (travados.Count != ids.Count) return Falhas.NaoEncontrado<IReadOnlyList<MovimentacaoDto>>();
      foreach (var id in ids)
        if (Falhas.EstaFechado(await _execucao.ObterPedidoDoNoAsync(id, ct)))
          return Falhas.PedidoFechado<IReadOnlyList<MovimentacaoDto>>();

      // Os pais entram no estado (Roteiro, para o destino de montagem), mas nao sao travados: a entrega
      // so le o Roteiro deles, nao escreve nada neles.
      var paiIds = travados.Where(n => n.EstruturaPaiId is not null).Select(n => n.EstruturaPaiId!.Value)
          .Except(ids).Distinct().ToList();
      var pais = await _execucao.ListarNosAsync(paiIds, ct);
      var estado = await _leitor.CarregarAsync([.. travados, .. pais], ct);

      var setorIds = origens.Select(o => o.SetorId!.Value)
          .Concat(itens.Where(i => i.DestinoSetorId is not null).Select(i => i.DestinoSetorId!.Value))
          .Distinct().ToList();
      var nomes = (await _catalogo.ObterSetoresPorIdAsync(setorIds, ct)).ToDictionary(s => s.Id, s => s.Nome);
      string NomeDoSetor(int id) => nomes.TryGetValue(id, out var nome) ? nome : $"Setor {id}";

      var consumido = new Dictionary<(int Item, Local Local), decimal>();
      var movimentos = new List<Movimentacao>();
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
        movimentos.Add(NovoMovimento.De(
            item.EstruturaItemId, TiposDeMovimentacao.Entrega, item.Quantidade, origem, destino!.Value, usuarioId));
      }

      foreach (var movimento in movimentos) _execucao.Adicionar(movimento);
      await _execucao.SalvarAlteracoesAsync(ct);
      return Result<IReadOnlyList<MovimentacaoDto>>.Ok(await _projetor.ProjetarMovimentacoesAsync(movimentos, ct));
    }, ct);
  }

  private static Local? OrigemValida(OrigemDaEntregaDto? origem) => origem switch
  {
    { Posicao: Posicoes.AguardandoColeta, SetorId: int s, Ordem: int k } => Local.AguardandoColeta(s, k),
    { Posicao: Posicoes.AguardandoMontagem, SetorId: int s, Ordem: null } => Local.AguardandoMontagem(s),
    _ => null,
  };

  private static string Onde(Local origem, Func<int, string> nomeDoSetor) =>
      origem.Posicao == Posicoes.AguardandoColeta
          ? $"aguardando coleta no Setor {nomeDoSetor(origem.SetorId!.Value)} (passo {origem.Ordem})"
          : $"aguardando montagem no Setor {nomeDoSetor(origem.SetorId!.Value)}";

  /// <summary>A tabela da spec secao 4.3, linha a linha.</summary>
  private static (Local? Destino, Recusa? Recusa) Destino(
      EstadoDeExecucao estado, ItemDaEntregaDto item, Local origem, Func<int, string> nomeDoSetor)
  {
    var id = item.EstruturaItemId;
    var nome = estado.Nome(id);

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
          return ParaAMontagem(estado, item, calculado.PaiId!.Value, nomeDoSetor);
      }
    }

    // Redirecionamento: o que ja aguarda montagem vai aguardar em outro Setor do Roteiro do pai.
    if (item.DestinoSetorId is int mesmo && mesmo == origem.SetorId)
      return (null, Indevido($"{nome} já aguarda montagem no Setor {nomeDoSetor(mesmo)}."));
    if (estado.Calc.No(id).PaiId is not int paiId)
      return (null, new Recusa(CodigosDaExecucao.OrigemInvalida, TipoDeErro.Validacao,
          $"{nome} é uma Peça: não aguarda montagem de ninguém."));
    return ParaAMontagem(estado, item, paiId, nomeDoSetor);
  }

  /// <summary>
  /// Ordem das duas checagens fixada pela spec secao 4.3: pai sem Roteiro sai primeiro, mesmo com
  /// destino nulo — a tela de D7 mostra um Item pronto cujo pai nao tem Roteiro sem Setor nenhum para
  /// escolher, entao o pedido natural chega sem destino e tem de cair em `PaiSemRoteiro`, nao em
  /// `DestinoIndevido`.
  /// </summary>
  private static (Local? Destino, Recusa? Recusa) ParaAMontagem(
      EstadoDeExecucao estado, ItemDaEntregaDto item, int paiId, Func<int, string> nomeDoSetor)
  {
    var possiveis = estado.Calc.SetoresDoRoteiro(paiId);
    if (possiveis.Count == 0)
      return (null, new Recusa(CodigosDaExecucao.PaiSemRoteiro, TipoDeErro.Conflito,
          $"{estado.Nome(paiId)} não tem Roteiro: o PCP precisa defini-lo antes de receber os filhos."));

    if (item.DestinoSetorId is not int destinoSetorId)
      return (null, Indevido(
          $"Escolha em que Setor {estado.Nome(item.EstruturaItemId)} vai aguardar a montagem de {estado.Nome(paiId)}."));

    if (!possiveis.Contains(destinoSetorId))
      return (null, new Recusa(CodigosDaExecucao.DestinoForaDoRoteiroDoPai, TipoDeErro.Conflito,
          $"O Setor {nomeDoSetor(destinoSetorId)} não está no Roteiro de {estado.Nome(paiId)}."));

    return (Local.AguardandoMontagem(destinoSetorId), null);
  }

  private static Recusa Indevido(string mensagem) =>
      new(CodigosDaExecucao.DestinoIndevido, TipoDeErro.Validacao, mensagem);
}

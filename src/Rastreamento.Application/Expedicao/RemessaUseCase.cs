using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Expedicao;

/// <summary>
/// A remessa (C8, C9, C10, C12, C13, D8 e D9 da spec da Fase 5A, secao 3.5): expedir uma Peca ou um lote de Pecas
/// numa remessa so, tudo ou nada, e estornar a remessa inteira. A remessa escolhe sozinha de onde tira, na ordem de
/// C10 (<see cref="CalculoDaExpedicao.Tirar"/>), e grava uma linha `Expedicao` por posicao de origem, todas
/// apontando a `dbo.Remessa`. Nenhuma situacao de afericao recusa (D8): os avisos voltam no `201` (decisao P5 do
/// plano de backend da Fase 5A). As duas escritas correm na transacao da execucao, travam os nos e depois os
/// Pedidos (spec da Fase 3, secao 8.1) e recalculam o fechamento no fim (C12; o estorno reabre, C13).
/// </summary>
public sealed class RemessaUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly IExpedicaoRepository _expedicao;
  private readonly IReceitaPadraoRepository _catalogo;
  private readonly LeitorDeEstado _leitor;
  private readonly ProjetorDoLivro _projetor;
  private readonly FechamentoDoPedido _fechamento;

  public RemessaUseCase(
      IExecucaoRepository execucao, IExpedicaoRepository expedicao, IEstruturaRepository estruturas,
      IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _expedicao = expedicao;
    _catalogo = catalogo;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _projetor = new ProjetorDoLivro(execucao, catalogo);
    _fechamento = new FechamentoDoPedido(execucao);
  }

  /// <summary>O que passou pelas regras de UMA Peca do lote: de onde ela tira e o que avisar.</summary>
  private sealed record PecaDaRemessa(int PecaId, IReadOnlyList<Tirada> Tiradas, IReadOnlyList<AvisoDeExpedicao> Avisos);

  /// <summary>
  /// Tudo ou nada. Antes da transacao, as recusas de entrada do lote da execucao, nesta ordem: lista vazia, teto,
  /// quantidade de cada item, Peca repetida. Dentro dela, todas as travas (os nos, depois os Pedidos das Pecas) e
  /// cada item, na ordem do corpo: existe (404), e Peca, Pedido nao fechado, cabe no expedivel. So com todos
  /// validos grava a remessa e as linhas, e recalcula os Pedidos. A pausa do Pedido nao recusa (regra 31).
  /// </summary>
  public async Task<Result<ResultadoDaRemessaDto>> Expedir(NovaRemessaDto dto, int usuarioId, CancellationToken ct)
  {
    var itens = dto.Itens ?? [];
    if (itens.Count == 0) return Falhas.LoteVazio<ResultadoDaRemessaDto>();
    if (itens.Count > ApontamentoUseCase.TamanhoMaximoDoLote) return Falhas.LoteGrandeDemais<ResultadoDaRemessaDto>();
    foreach (var item in itens)
      // `CalculoDaExpedicao.Tirar` lanca com quantidade nao positiva: a recusa vem antes dele.
      if (!Quantidades.CabeNaColuna(item.Quantidade))
        return Falhas.QuantidadeInvalida<ResultadoDaRemessaDto>(item.Quantidade);
    var repetida = itens.GroupBy(i => i.EstruturaItemId).FirstOrDefault(g => g.Count() > 1);
    if (repetida is not null) return await RepetidaAsync(repetida.Key, ct);

    return await _execucao.ExecutarAsync(async () =>
    {
      var travados = await _execucao.TravarNosAsync(itens.Select(i => i.EstruturaItemId).Distinct().Order().ToList(), ct);
      // Os Pedidos so das Pecas, todos antes da primeira leitura de Pedido: lido antes, a trava converteria S em U
      // e deadlockaria (`PK_Pedido`, como no `TravaDaPeca`).
      var pecaIds = travados.Where(n => n.EstruturaPaiId is null).Select(n => n.Id).ToList();
      var pedidosTravados = pecaIds.Count > 0 ? await _execucao.TravarPedidosDosNosAsync(pecaIds, ct) : [];
      var pedidos = await _expedicao.ListarPedidosDasPecasAsync(pecaIds, ct);
      var afericoes = (await _expedicao.ListarAfericoesAsync(pecaIds, ct)).ToLookup(a => a.EstruturaItemId);
      var estado = await _leitor.CarregarAsync(travados, ct);

      var validas = new List<PecaDaRemessa>(itens.Count);
      foreach (var item in itens)
      {
        var no = travados.FirstOrDefault(n => n.Id == item.EstruturaItemId);
        if (no is null) return Falhas.NaoEncontrado<ResultadoDaRemessaDto>();
        var nome = estado.Nome(no.Id);
        if (no.EstruturaPaiId is not null)
          return Falhas.Validacao<ResultadoDaRemessaDto>(CodigosDaExpedicao.NaoEhPeca,
              $"{nome} é um Item: só a Peça chega ao local de expedição.");
        // O mesmo criterio de `Falhas.EstaFechado`; a pausa nao recusa (regra 31).
        if (pedidos.GetValueOrDefault(no.Id) is not { Status: not (StatusConcluido or StatusCancelado) })
          return Falhas.Conflito<ResultadoDaRemessaDto>(CodigosDaExecucao.PedidoFechado,
              $"O Pedido de {nome} já foi concluído ou cancelado: não se expede mais nada dele.");

        var posicoes = CalculoDaExpedicao.Posicoes(estado.Calc.Saldos(no.Id));
        var tiradas = CalculoDaExpedicao.Tirar(posicoes, item.Quantidade);
        if (tiradas is null) return AcimaDoExpedivel(nome, posicoes);

        var situacao = CalculoDaExpedicao.Situacao(no.RequerRelatorioDimensional, CalculoDaExpedicao.AfericaoValida(afericoes[no.Id]));
        validas.Add(new PecaDaRemessa(no.Id, tiradas, CalculoDaExpedicao.Avisos(situacao, tiradas)));
      }

      var agora = DateTime.UtcNow;
      var remessa = new Remessa { DataHora = agora, UsuarioId = usuarioId };
      // Duas gravacoes na mesma transacao: a primeira da o Id que as linhas apontam
      // (`CK_Movimentacao_RegistroCoerente` exige o vinculo). Quem falhar depois desfaz as duas.
      _expedicao.Adicionar(remessa);
      await _execucao.SalvarAlteracoesAsync(ct);
      foreach (var peca in validas)
        foreach (var tirada in peca.Tiradas)
          _execucao.Adicionar(Linha(peca.PecaId, TiposDeMovimentacao.Expedicao, tirada.Quantidade,
              new Local(tirada.Posicao, null, null), Local.Expedido, remessa.Id, agora, usuarioId));
      await _execucao.SalvarAlteracoesAsync(ct);
      await _fechamento.RecalcularAsync(pedidosTravados, agora, ct);

      // Recem-gravada: quem le e o autor. O status dos Pedidos e o de depois do recalculo.
      var projetada = (await ProjetarAsync([remessa], new QuemLe(usuarioId, false), ct)).Single();
      var avisos = validas
          .SelectMany(p => p.Avisos.Select(a => new AvisoDaPecaDto(p.PecaId, a.Tipo, a.Quantidade)))
          .ToList();
      return Result<ResultadoDaRemessaDto>.Ok(new ResultadoDaRemessaDto(projetada, avisos));
    }, ct);
  }

  /// <summary>
  /// Estorna a remessa inteira (C8; secao 3.5): um `Estorno` por linha dela, de `Expedido` de volta a origem, todos
  /// apontando a remessa, numa transacao. Ordem: remessa (404), autor ou PCP/Administrador (403), Pedido
  /// `Cancelado` (409; o `Concluido` e aceito e reabre, C13), ja estornada (409, relido depois da trava). Nada
  /// mais o impede: `Expedido` e terminal, entao a quantidade expedida nunca andou depois.
  /// </summary>
  public async Task<Result<IReadOnlyList<MovimentacaoDto>>> Estornar(
      int remessaId, int usuarioId, bool podeEstornarDeOutros, CancellationToken ct)
  {
    var remessa = await _expedicao.ObterRemessaAsync(remessaId, ct);
    if (remessa is null) return Falhas.NaoEncontrado<IReadOnlyList<MovimentacaoDto>>();
    if (remessa.UsuarioId != usuarioId && !podeEstornarDeOutros) return Falhas.Proibido<IReadOnlyList<MovimentacaoDto>>();

    return await _execucao.ExecutarAsync(async () =>
    {
      // As linhas originais sao so de inclusao: lidas antes da trava so para saber quais Pecas travar.
      var originais = (await _expedicao.ListarLinhasDasRemessasAsync([remessaId], ct))
          .Where(l => l.Tipo == TiposDeMovimentacao.Expedicao).ToList();
      var pecaIds = originais.Select(l => l.EstruturaItemId).Distinct().Order().ToList();
      await _execucao.TravarNosAsync(pecaIds, ct);
      var pedidosTravados = pecaIds.Count > 0 ? await _execucao.TravarPedidosDosNosAsync(pecaIds, ct) : [];

      var cancelados = (await _expedicao.ListarPedidosDasPecasAsync(pecaIds, ct)).Values
          .Where(p => p.Status == StatusCancelado).Select(p => p.Numero).Distinct().Order().ToList();
      if (cancelados.Count > 0)
        return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.PedidoFechado,
            $"O Pedido {string.Join(", ", cancelados)} foi cancelado: a remessa dele não se estorna.");

      // Relido DEPOIS da trava: dois estornos simultaneos da mesma remessa travam as mesmas Pecas, e o segundo ve
      // o primeiro aqui (`UX_Movimentacao_EstornoDe` e a segunda rede).
      if ((await _execucao.ListarEstornadasAsync(originais.Select(l => l.Id).ToList(), ct)).Count > 0)
        return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.JaEstornado, "Esta remessa já foi estornada.");

      var agora = DateTime.UtcNow;
      var estornos = originais.Select(l =>
      {
        var estorno = Linha(l.EstruturaItemId, TiposDeMovimentacao.Estorno, l.Quantidade, Local.Expedido,
            Local.DaOrigem(l), remessaId, agora, usuarioId);
        estorno.EstornoDeId = l.Id;
        return estorno;
      }).ToList();
      foreach (var estorno in estornos) _execucao.Adicionar(estorno);
      await _execucao.SalvarAlteracoesAsync(ct);
      await _fechamento.RecalcularAsync(pedidosTravados, agora, ct);
      return Result<IReadOnlyList<MovimentacaoDto>>.Ok(await _projetor.ProjetarMovimentacoesAsync(estornos, ct));
    }, ct);
  }

  /// <summary>
  /// As remessas com as linhas `Expedicao` de cada uma (os estornos nao viram linha: so marcam `Estornada`), na
  /// ordem recebida. Consultas em lote, nunca uma por remessa nem por Peca. `PedidoConcluido` e o status de agora.
  /// `Estornavel`: nao estornada, de nenhum Pedido `Cancelado` (C13) e de quem le, ou quem le ve as de todos (C7).
  /// </summary>
  internal async Task<IReadOnlyList<RemessaDto>> ProjetarAsync(
      IReadOnlyList<Remessa> remessas, QuemLe quem, CancellationToken ct)
  {
    if (remessas.Count == 0) return [];
    var todas = await _expedicao.ListarLinhasDasRemessasAsync(remessas.Select(r => r.Id).ToList(), ct);
    var linhas = todas.Where(l => l.Tipo == TiposDeMovimentacao.Expedicao).ToList();
    var estornadas = todas.Where(l => l.Tipo == TiposDeMovimentacao.Estorno).Select(l => l.RemessaId!.Value).ToHashSet();
    var pecaIds = linhas.Select(l => l.EstruturaItemId).Distinct().ToList();

    var pedidos = await _expedicao.ListarPedidosDasPecasAsync(pecaIds, ct);
    var nos = (await _execucao.ListarNosAsync(pecaIds, ct)).ToDictionary(n => n.Id);
    var componenteIds = nos.Values.Where(n => n.ComponenteId is not null).Select(n => n.ComponenteId!.Value).Distinct().ToList();
    var componentes = (await _catalogo.ObterComponentesPorIdAsync(componenteIds, ct)).ToDictionary(c => c.Id);
    var autores = await _execucao.ListarNomesDeUsuariosAsync(remessas.Select(r => r.UsuarioId).Distinct().ToList(), ct);

    return remessas.Select(r =>
    {
      var daRemessa = linhas.Where(l => l.RemessaId == r.Id).Select(l =>
      {
        var no = nos.GetValueOrDefault(l.EstruturaItemId);
        var componente = no?.ComponenteId is int c ? componentes.GetValueOrDefault(c) : null;
        var pedido = pedidos.GetValueOrDefault(l.EstruturaItemId);
        // A descricao pela regra 19 (a do no, senao a do Componente), como `EstadoDeExecucao.Nome`.
        return new LinhaDaRemessaDto(
            l.EstruturaItemId, no?.Descricao ?? componente?.Descricao ?? string.Empty, componente?.Codigo,
            pedido?.PedidoId ?? 0, pedido?.Numero ?? string.Empty, pedido?.Status == StatusConcluido,
            l.OrigemPosicao, l.Quantidade);
      }).ToList();
      var estornada = estornadas.Contains(r.Id);
      var estornavel = !estornada
          && (quem.VeRegistrosDeTodos || r.UsuarioId == quem.UsuarioId)
          && daRemessa.All(l => pedidos.GetValueOrDefault(l.PecaId)?.Status != StatusCancelado);
      return new RemessaDto(r.Id, r.DataHora, r.UsuarioId, autores.GetValueOrDefault(r.UsuarioId, string.Empty),
          estornada, estornavel, daRemessa);
    }).ToList();
  }

  // Os valores de `CK_Pedido_Status` que a remessa olha.
  private const string StatusConcluido = "Concluido";
  private const string StatusCancelado = "Cancelado";

  /// <summary>
  /// A recusa da Peca repetida nomeia a Peca (regra 19), lida fora da transacao so para a frase. No inexistente
  /// fica com o 404, como dentro dela.
  /// </summary>
  private async Task<Result<ResultadoDaRemessaDto>> RepetidaAsync(int noId, CancellationToken ct)
  {
    var nos = await _execucao.ListarNosAsync([noId], ct);
    if (nos.Count == 0) return Falhas.NaoEncontrado<ResultadoDaRemessaDto>();
    var nome = (await _leitor.CarregarAsync(nos, ct)).Nome(noId);
    return Falhas.Validacao<ResultadoDaRemessaDto>(CodigosDaExecucao.ItemRepetido,
        $"{nome} aparece mais de uma vez na remessa: some as quantidades num item só.");
  }

  /// <summary>A recusa do que passa do expedivel (C10), com o que espera a Gestao quando ha.</summary>
  private static Result<ResultadoDaRemessaDto> AcimaDoExpedivel(string nome, PosicoesDaPeca p)
  {
    var frase = $"Só dá para expedir {Quantidades.Formatar(p.Expedivel)} de {nome}";
    if (p.AguardandoLiberacao > 0)
      frase += $"; {Quantidades.Formatar(p.AguardandoLiberacao)} aguardam a resposta da Gestão a um pedido de liberação "
          + "e não saem antes dela";
    return Falhas.Conflito<ResultadoDaRemessaDto>(CodigosDaExecucao.SaldoInsuficiente, frase + ".");
  }

  private static Movimentacao Linha(
      int pecaId, string tipo, decimal quantidade, Local de, Local para, int remessaId, DateTime agora, int usuarioId)
  {
    var linha = NovoMovimento.De(pecaId, tipo, quantidade, de, para, usuarioId);
    linha.RemessaId = remessaId;
    linha.DataHora = agora;
    return linha;
  }
}

using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Baixa;

/// <summary>
/// Dar baixa num no (secoes 4.1 a 4.6 e 5.1 a 5.4 da spec da Fase 5B-1): uma `dbo.Perda` por baixa e uma linha `Perda`
/// do livro por no e posicao de origem, todas apontando a baixa (D10, C7), e a previa do mesmo sem gravar (C14). A conta
/// e uma so, `CalculoDaBaixa.Planejar`: a escrita a chama dentro da transacao, depois das travas, e a previa fora dela,
/// sem travas (decisao P4 do plano de backend da Fase 5B-1).
///
/// A calculadora enxerga a arvore inteira da Peca do topo do no: a cascata C6 a partir da Peca (`IncluirTopo`) desce
/// por todos os filhos dela, e a trava no topo (D13, D14) sobe pela cadeia. A escrita trava todos os nos dessa arvore,
/// em ordem crescente de Id, e depois o Pedido (spec da Fase 3, secao 8.1), e recalcula o fechamento na mesma
/// transacao (C10). A pausa do Pedido nao recusa (regra 31, C13).
/// </summary>
public sealed class BaixaUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly IBaixaRepository _baixas;
  private readonly IReceitaPadraoRepository _catalogo;
  private readonly LeitorDeEstado _leitor;
  private readonly FechamentoDoPedido _fechamento;

  public BaixaUseCase(
      IExecucaoRepository execucao, IBaixaRepository baixas, IEstruturaRepository estruturas,
      IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _baixas = baixas;
    _catalogo = catalogo;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _fechamento = new FechamentoDoPedido(execucao);
  }

  /// <summary>O corpo validado: motivo conhecido, observacao aparada (nula quando vazia) e as origens como `Local`.</summary>
  private sealed record Entrada(
      string Motivo, string? Observacao, bool Tudo, IReadOnlyList<OrigemDaBaixa> Origens, bool IncluirTopo);

  /// <summary>O plano da baixa sobre o estado lido, com o Pedido do no.</summary>
  private sealed record Planejada(PlanoDaBaixa Plano, EstadoDeExecucao Estado, int PedidoId);

  /// <summary>
  /// Grava a baixa. Antes da transacao, as recusas do corpo (motivo, observacao, teto de origens, quantidade); dentro
  /// dela, as travas, o Pedido `Cancelado` (409 `PedidoFechado`) e a conta, que recusa a origem, a posicao repetida,
  /// o saldo e o "tudo" sem nada em producao. `201` com a baixa projetada; quem le e o autor.
  /// </summary>
  public async Task<Result<BaixaDto>> DarBaixa(int noId, NovaBaixaDto dto, int usuarioId, CancellationToken ct)
  {
    var validada = Validar(dto);
    if (!validada.Sucesso) return Falhas.Repassar<BaixaDto, Entrada>(validada);
    var entrada = validada.Valor!;

    return await _execucao.ExecutarAsync(async () =>
    {
      var planejada = await PlanejarAsync(noId, entrada, travar: true, ct);
      if (!planejada.Sucesso) return Falhas.Repassar<BaixaDto, Planejada>(planejada);
      var (plano, _, pedidoId) = planejada.Valor!;

      var agora = DateTime.UtcNow;
      var perda = new Perda
      {
        Motivo = entrada.Motivo, Observacao = entrada.Observacao, PedidoId = pedidoId, EstruturaItemId = noId,
        Tudo = entrada.Tudo, DataHora = agora, UsuarioId = usuarioId,
      };
      // Duas gravacoes na mesma transacao, como a remessa: a primeira da o Id que as linhas apontam
      // (`CK_Movimentacao_RegistroCoerente` exige o vinculo). Quem falhar depois desfaz as duas.
      _baixas.Adicionar(perda);
      await _execucao.SalvarAlteracoesAsync(ct);
      foreach (var linha in plano.Linhas)
      {
        var movimento = NovoMovimento.De(
            linha.NoId, TiposDeMovimentacao.Perda, linha.Quantidade, linha.Origem, Local.Perdido, usuarioId);
        movimento.PerdaId = perda.Id;
        movimento.DataHora = agora;
        _execucao.Adicionar(movimento);
      }
      await _execucao.SalvarAlteracoesAsync(ct);
      await _fechamento.RecalcularAsync([pedidoId], agora, ct);

      return Result<BaixaDto>.Ok((await ProjetarAsync([perda], new QuemLe(usuarioId, false), ct)).Single());
    }, ct);
  }

  /// <summary>
  /// O que <see cref="DarBaixa"/> gravaria, com as mesmas recusas, sem travar nem gravar nada (C14): as linhas, o
  /// fabricado que passa a sobrar (C6) e a trava no topo (D13, D14).
  /// </summary>
  public async Task<Result<PreviaDaBaixaDto>> PreverBaixa(int noId, NovaBaixaDto dto, CancellationToken ct)
  {
    var validada = Validar(dto);
    if (!validada.Sucesso) return Falhas.Repassar<PreviaDaBaixaDto, Entrada>(validada);
    var entrada = validada.Valor!;

    return await _execucao.ConsultarAsync(async () =>
    {
      var planejada = await PlanejarAsync(noId, entrada, travar: false, ct);
      if (!planejada.Sucesso) return Falhas.Repassar<PreviaDaBaixaDto, Planejada>(planejada);
      var (plano, estado, _) = planejada.Valor!;

      var setores = await NomesDosSetoresAsync(plano.Linhas.Select(l => l.Origem.SetorId).OfType<int>(), ct);
      var linhas = plano.Linhas.Select(l => new LinhaDaBaixaDto(
          l.NoId, estado.Nome(l.NoId), estado.Codigos.GetValueOrDefault(l.NoId), l.Origem.Posicao, l.Origem.SetorId,
          l.Origem.SetorId is int s ? setores.GetValueOrDefault(s) : null, l.Origem.Ordem, l.Quantidade)).ToList();
      var sobra = plano.ViramSobra.Select(v => new SobraDaBaixaDto(v.NoId, estado.Nome(v.NoId), v.Quantidade)).ToList();
      var trava = plano.Trava is { } t
          ? new TravaNoTopoDto(t.PecaId, estado.Nome(t.PecaId), t.Quantidade, t.AIniciar)
          : null;
      return Result<PreviaDaBaixaDto>.Ok(new PreviaDaBaixaDto(linhas, sobra, trava));
    }, ct);
  }

  /// <summary>
  /// As baixas com as linhas `Perda` de cada uma, na ordem do livro (os estornos nao viram linha: so marcam
  /// `Estornada`), na ordem recebida. Consultas em lote, nunca uma por baixa nem por no. `Estornavel`, como na remessa:
  /// nao estornada, de Pedido que nao esta `Cancelado` (secao 4.9) e de quem le, ou quem le ve as de todos (C8).
  /// </summary>
  internal async Task<IReadOnlyList<BaixaDto>> ProjetarAsync(IReadOnlyList<Perda> perdas, QuemLe quem, CancellationToken ct)
  {
    if (perdas.Count == 0) return [];
    var todas = await _baixas.ListarLinhasDasPerdasAsync(perdas.Select(p => p.Id).ToList(), ct);
    var linhas = todas.Where(l => l.Tipo == TiposDeMovimentacao.Perda).ToList();
    var estornadas = todas.Where(l => l.Tipo == TiposDeMovimentacao.Estorno).Select(l => l.PerdaId!.Value).ToHashSet();

    var nos = (await _execucao.ListarNosAsync(linhas.Select(l => l.EstruturaItemId).Distinct().ToList(), ct))
        .ToDictionary(n => n.Id);
    var componenteIds = nos.Values.Where(n => n.ComponenteId is not null).Select(n => n.ComponenteId!.Value).Distinct().ToList();
    var componentes = (await _catalogo.ObterComponentesPorIdAsync(componenteIds, ct)).ToDictionary(c => c.Id);
    var setores = await NomesDosSetoresAsync(linhas.Select(l => l.OrigemSetorId).OfType<int>(), ct);
    var autores = await _execucao.ListarNomesDeUsuariosAsync(perdas.Select(p => p.UsuarioId).Distinct().ToList(), ct);
    var status = await _baixas.ListarStatusDosPedidosAsync(perdas.Select(p => p.PedidoId).Distinct().ToList(), ct);

    return perdas.Select(p =>
    {
      var daBaixa = linhas.Where(l => l.PerdaId == p.Id).Select(l =>
      {
        var no = nos.GetValueOrDefault(l.EstruturaItemId);
        var componente = no?.ComponenteId is int c ? componentes.GetValueOrDefault(c) : null;
        // A descricao pela regra 19 (a do no, senao a do Componente), como `EstadoDeExecucao.Nome`.
        return new LinhaDaBaixaDto(
            l.EstruturaItemId, no?.Descricao ?? componente?.Descricao ?? string.Empty, componente?.Codigo,
            l.OrigemPosicao, l.OrigemSetorId, l.OrigemSetorId is int s ? setores.GetValueOrDefault(s) : null,
            l.OrigemOrdem, l.Quantidade);
      }).ToList();
      var estornada = estornadas.Contains(p.Id);
      var estornavel = !estornada
          && (quem.VeRegistrosDeTodos || p.UsuarioId == quem.UsuarioId)
          && status.GetValueOrDefault(p.PedidoId) != StatusCancelado;
      return new BaixaDto(
          p.Id, p.Motivo, p.Observacao, Escopo(p), p.Tudo, p.DataHora, p.UsuarioId,
          autores.GetValueOrDefault(p.UsuarioId, string.Empty), estornada, estornavel, daBaixa);
    }).ToList();
  }

  // O valor de `CK_Pedido_Status` que a baixa recusa (o `Concluido` aceita a sobra viva, C12).
  private const string StatusCancelado = "Cancelado";

  private static EscopoDaBaixaDto Escopo(Perda p) => p switch
  {
    { EstruturaItemId: int no } => new EscopoDaBaixaDto("No", no),
    { AgrupamentoId: int agrupamento } => new EscopoDaBaixaDto("Agrupamento", agrupamento),
    _ => new EscopoDaBaixaDto("Pedido", p.PedidoId),
  };

  /// <summary>
  /// As recusas do corpo que nao dependem do estado, nesta ordem: motivo, observacao (aparada, ate 500, C19), teto de
  /// origens (C18), posicao conhecida e quantidade. A origem que nao serve, a repetida e o `IncluirTopo` fora de lugar
  /// sao da conta (`CalculoDaBaixa.Planejar`).
  /// </summary>
  private static Result<Entrada> Validar(NovaBaixaDto dto)
  {
    if (dto.Motivo is not { } motivo || !MotivosDePerda.Todos.Contains(motivo))
      return Falhas.Validacao<Entrada>(CodigosDaBaixa.MotivoInvalido,
          $"O motivo da baixa é um destes: {string.Join(", ", MotivosDePerda.Todos)}.");

    var observacao = dto.Observacao?.Trim();
    if (string.IsNullOrEmpty(observacao)) observacao = null;
    if (observacao is not null && observacao.Length > CodigosDaExpedicao.TamanhoMaximoDoTexto)
      return Falhas.Validacao<Entrada>(CodigosDaExpedicao.TextoLongoDemais,
          $"O texto tem no máximo {CodigosDaExpedicao.TamanhoMaximoDoTexto} caracteres.");

    var origens = dto.Origens ?? [];
    if (origens.Count > ApontamentoUseCase.TamanhoMaximoDoLote) return Falhas.LoteGrandeDemais<Entrada>();

    var locais = new List<OrigemDaBaixa>(origens.Count);
    foreach (var origem in origens)
    {
      if (origem.Posicao is not { } posicao || !Posicoes.Todas.Contains(posicao))
        return Falhas.Validacao<Entrada>(CodigosDaBaixa.OrigemInvalida, "Diga de que posição sai a baixa.");
      if (origem.Quantidade <= 0m || !Quantidades.CabeNaColuna(origem.Quantidade))
        return Falhas.QuantidadeInvalida<Entrada>(origem.Quantidade);
      locais.Add(new OrigemDaBaixa(new Local(posicao, origem.SetorId, origem.Ordem), origem.Quantidade));
    }

    return Result<Entrada>.Ok(new Entrada(motivo, observacao, dto.Tudo, locais, dto.IncluirTopo));
  }

  /// <summary>
  /// Le a cadeia do no ate a Peca do topo e a arvore inteira da Peca, carrega o estado e chama a conta. Na escrita
  /// (`travar`), as leituras correm dentro da transacao SERIALIZABLE, que segura a arvore lida; os nos sao travados um a
  /// um em ordem crescente de Id e o Pedido depois deles, antes da primeira leitura dele. Na previa, nada se trava.
  /// </summary>
  private async Task<Result<Planejada>> PlanejarAsync(int noId, Entrada entrada, bool travar, CancellationToken ct)
  {
    var cadeia = await _baixas.ListarCadeiaAteOTopoAsync(noId, ct);
    var topo = cadeia.Count > 0 ? cadeia[^1] : noId;
    var arvore = await _baixas.ListarSubarvoreAsync(topo, ct);
    if (!arvore.Contains(noId)) return Falhas.NaoEncontrado<Planejada>();

    var nos = travar ? await _execucao.TravarNosAsync(arvore, ct) : await _execucao.ListarNosAsync(arvore, ct);
    if (nos.All(n => n.Id != noId)) return Falhas.NaoEncontrado<Planejada>();
    if (travar) await _execucao.TravarPedidosDosNosAsync([noId], ct);

    var pedido = await _execucao.ObterPedidoDoNoAsync(noId, ct);
    if (pedido is null) return Falhas.NaoEncontrado<Planejada>();
    if (pedido.Status == StatusCancelado)
      return Falhas.Conflito<Planejada>(CodigosDaExecucao.PedidoFechado,
          $"O Pedido {pedido.Numero} foi cancelado: não se dá baixa nele.");

    var estado = await _leitor.CarregarAsync(nos, ct);
    var setores = await NomesDosSetoresAsync(entrada.Origens.Select(o => o.Local.SetorId).OfType<int>(), ct);
    var plano = CalculoDaBaixa.Planejar(
        estado.Calc, new PedidoDeBaixa(noId, entrada.Motivo, entrada.Tudo, entrada.Origens, entrada.IncluirTopo, cadeia),
        estado.Nome, id => setores.GetValueOrDefault(id) ?? $"Setor {id}");
    if (!plano.Sucesso) return Falhas.Repassar<Planejada, PlanoDaBaixa>(plano);
    return Result<Planejada>.Ok(new Planejada(plano.Valor!, estado, pedido.PedidoId));
  }

  private async Task<IReadOnlyDictionary<int, string>> NomesDosSetoresAsync(IEnumerable<int> ids, CancellationToken ct)
  {
    var distintos = ids.Distinct().ToList();
    return distintos.Count == 0
        ? new Dictionary<int, string>()
        : (await _catalogo.ObterSetoresPorIdAsync(distintos, ct)).ToDictionary(s => s.Id, s => s.Nome);
  }
}

using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Expedicao;

/// <summary>
/// As leituras da expedicao (secao 5.1 e C20 da spec da Fase 5A): a lista da Qualidade, o historico da Peca, os
/// pedidos de liberacao pendentes (da Gestao) e as remessas recentes. Todo metodo publico passa por
/// `ConsultarAsync`, como as leituras da execucao, e le em lote: nunca uma consulta por Peca nem por registro.
/// "Estornavel" aqui e o do livro para quem le (o autor, ou PCP/Administrador, e ainda nao estornado); as demais
/// recusas do estorno (saldo que andou, Pedido `Cancelado`...) ficam com a escrita.
/// </summary>
public sealed class ConsultaDaExpedicaoUseCase
{
  /// <summary>Decisao P9 do plano de backend da Fase 5A: as remessas recentes sao as dos ultimos 30 dias, contados de `DateTime.UtcNow`.</summary>
  public const int DiasDeRemessasRecentes = 30;

  private readonly IExecucaoRepository _execucao;
  private readonly IExpedicaoRepository _expedicao;
  private readonly IReceitaPadraoRepository _catalogo;
  private readonly TimeProvider _relogio;
  private readonly LeitorDeEstado _leitor;
  private readonly RemessaUseCase _remessa;

  public ConsultaDaExpedicaoUseCase(
      IExecucaoRepository execucao, IExpedicaoRepository expedicao, IEstruturaRepository estruturas,
      IReceitaPadraoRepository catalogo, TimeProvider relogio)
  {
    _execucao = execucao;
    _expedicao = expedicao;
    _catalogo = catalogo;
    _relogio = relogio;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    // So para `ProjetarAsync`: as remessas recentes saem do mesmo projetor que o `201` de `POST /remessas`.
    _remessa = new RemessaUseCase(execucao, expedicao, estruturas, catalogo);
  }

  /// <summary>
  /// A lista da Qualidade: as Pecas (raizes) de Pedido nao encerrado com alguma quantidade nas cinco posicoes do
  /// local de expedicao ou nas tres do conserto, agrupadas por Pedido do prazo de entrega mais proximo ao mais
  /// distante (`Id` do Pedido e da Peca desempatando). Pedido pausado continua nela, com a pausa: a pausa recusa so o
  /// inicio (regra 31).
  /// </summary>
  public Task<Result<ExpedicaoDto>> Lista(QuemLe quem, CancellationToken ct) =>
      _execucao.ConsultarAsync(() => ListaAsync(quem, ct), ct);

  /// <summary>
  /// O historico da Peca, mais recente primeiro (`DataHora`, e o `Id` desempatando): aferições, contagens,
  /// consertos, pedidos e respostas de liberacao e remessas. Estorno nao e registro a parte, so marca o original como
  /// estornado. Uma remessa e UM registro, com a soma das linhas dela nesta Peca, e nunca e estornavel aqui (C20).
  /// </summary>
  public Task<Result<IReadOnlyList<RegistroDaExpedicaoDto>>> Historico(int pecaId, QuemLe quem, CancellationToken ct) =>
      _execucao.ConsultarAsync(() => HistoricoAsync(pecaId, quem, ct), ct);

  /// <summary>
  /// Os pedidos de liberacao que a Gestao ainda nao respondeu, do mais antigo para o mais novo. A regra de
  /// "pendente" e a de <see cref="LiberacaoUseCase.EstaPendente"/>.
  /// </summary>
  public Task<Result<IReadOnlyList<LiberacaoPendenteDto>>> LiberacoesPendentes(CancellationToken ct) =>
      _execucao.ConsultarAsync(() => LiberacoesPendentesAsync(ct), ct);

  /// <summary>As remessas dos ultimos <see cref="DiasDeRemessasRecentes"/> dias, mais recente primeiro, com as linhas (C20).</summary>
  public Task<Result<IReadOnlyList<RemessaDto>>> RemessasRecentes(QuemLe quem, CancellationToken ct) =>
      _execucao.ConsultarAsync(async () =>
      {
        var remessas = await _expedicao.ListarRemessasDesdeAsync(DateTime.UtcNow.AddDays(-DiasDeRemessasRecentes), ct);
        return Result<IReadOnlyList<RemessaDto>>.Ok(await _remessa.ProjetarAsync(remessas, quem, ct));
      }, ct);

  // ---------------------------------------------------------------- lista

  private async Task<Result<ExpedicaoDto>> ListaAsync(QuemLe quem, CancellationToken ct)
  {
    var raizes = (await _execucao.ListarNosEmProducaoAsync(ct)).Where(x => x.No.EstruturaPaiId is null).ToList();
    if (raizes.Count == 0) return Result<ExpedicaoDto>.Ok(new ExpedicaoDto([]));

    var estado = await _leitor.CarregarAsync(raizes.Select(x => x.No).ToList(), ct);
    var candidatas = raizes
        .Select(x => (Contexto: x, Posicoes: CalculoDaExpedicao.Posicoes(estado.Calc.Saldos(x.No.Id))))
        .Where(t => t.Posicoes.NoLocalDeExpedicao > 0m || t.Posicoes.EmConserto > 0m)
        .ToList();
    if (candidatas.Count == 0) return Result<ExpedicaoDto>.Ok(new ExpedicaoDto([]));

    var pecaIds = candidatas.Select(t => t.Contexto.No.Id).ToList();
    var afericoes = (await _expedicao.ListarAfericoesAsync(pecaIds, ct)).ToLookup(a => a.EstruturaItemId);
    var liberacoes = await _expedicao.ListarLiberacoesAsync(pecaIds, ct);
    var linhas = liberacoes.Count == 0
        ? []
        : await _expedicao.ListarLinhasDasLiberacoesAsync(liberacoes.Select(l => l.Id).ToList(), ct);
    var estornadas = LiberacaoUseCase.LinhasEstornadas(linhas);
    var porRegistro = linhas.ToLookup(l => l.LiberacaoId!.Value);
    var respostas = liberacoes.Where(l => l.Tipo == TiposDeLiberacao.Resposta).ToList();
    var pendentes = liberacoes
        .Where(l => LiberacaoUseCase.EstaPendente(l, respostas, estornadas, porRegistro))
        .GroupBy(l => l.EstruturaItemId)
        .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.DataHora).ThenByDescending(l => l.Id).First());

    var validas = candidatas.ToDictionary(
        t => t.Contexto.No.Id, t => CalculoDaExpedicao.AfericaoValida(afericoes[t.Contexto.No.Id]));
    var autores = await _execucao.ListarNomesDeUsuariosAsync(
        validas.Values.OfType<RelatorioDimensional>().Select(a => a.UsuarioId)
            .Concat(pendentes.Values.Select(p => p.UsuarioId)).Distinct().ToList(), ct);
    var setorIds = candidatas
        .SelectMany(t => estado.Calc.Saldos(t.Contexto.No.Id))
        .Where(s => s.Local.SetorId is not null && Posicoes.DoConserto.Contains(s.Local.Posicao) && s.Quantidade > 0m)
        .Select(s => s.Local.SetorId!.Value).Distinct().ToList();
    var setores = (await _catalogo.ObterSetoresPorIdAsync(setorIds, ct)).ToDictionary(s => s.Id, s => s.Nome);

    var hoje = PrazoDeEntrega.HojeEmBrasilia(_relogio);
    string Autor(int id) => autores.GetValueOrDefault(id, string.Empty);

    PecaDaExpedicaoDto Peca(ContextoDoNo x, PosicoesDaPeca posicoes)
    {
      var id = x.No.Id;
      var valida = validas[id];
      var consertos = estado.Calc.Saldos(id)
          .Where(s => s.Local.SetorId is not null && Posicoes.DoConserto.Contains(s.Local.Posicao) && s.Quantidade > 0m)
          .OrderBy(s => s.Local.SetorId).ThenBy(s => Livro.OrdemDaPosicao(s.Local.Posicao))
          .Select(s => new ConsertoDaPecaDto(
              s.Local.SetorId!.Value, setores.GetValueOrDefault(s.Local.SetorId.Value, string.Empty), s.Local.Posicao, s.Quantidade))
          .ToList();
      return new PecaDaExpedicaoDto(
          id, estado.Nome(id), estado.Codigos.GetValueOrDefault(id), x.AgrupamentoId, x.AgrupamentoCodigo, x.No.Quantidade,
          x.No.RequerRelatorioDimensional, CalculoDaExpedicao.Situacao(x.No.RequerRelatorioDimensional, valida).ToString(),
          valida is null
              ? null
              : new AfericaoDto(valida.Id, valida.Resultado!, valida.DataHora, valida.UsuarioId, Autor(valida.UsuarioId),
                  Estornada: false, Estornavel: Estornavel(quem, valida.UsuarioId, estornada: false)),
          PosicoesDaPecaDto.De(posicoes), consertos,
          pendentes.GetValueOrDefault(id) is { } p
              ? new LiberacaoPendenteDaPecaDto(p.Id, p.Quantidade, p.Texto ?? string.Empty, p.DataHora, Autor(p.UsuarioId))
              : null,
          posicoes.Expedivel);
    }

    var pedidos = candidatas
        .GroupBy(t => t.Contexto.PedidoId)
        .Select(g => g.OrderBy(t => t.Contexto.No.Id).ToList())
        .OrderBy(g => g[0].Contexto.PedidoDataEntrega).ThenBy(g => g[0].Contexto.PedidoId)
        .Select(g =>
        {
          var x = g[0].Contexto;
          return new PedidoDaExpedicaoDto(
              x.PedidoId, x.PedidoNumero, x.PedidoCliente, x.PedidoDataEntrega,
              PrazoDeEntrega.EstaAtrasado(x.PedidoDataEntrega, x.PedidoStatus, hoje), PausaResumoDto.De(x.Pausa),
              g.Select(t => Peca(t.Contexto, t.Posicoes)).ToList());
        })
        .ToList();
    return Result<ExpedicaoDto>.Ok(new ExpedicaoDto(pedidos));
  }

  // ---------------------------------------------------------------- historico

  private async Task<Result<IReadOnlyList<RegistroDaExpedicaoDto>>> HistoricoAsync(
      int pecaId, QuemLe quem, CancellationToken ct)
  {
    if ((await _execucao.ListarNosAsync([pecaId], ct)).Count == 0)
      return Falhas.NaoEncontrado<IReadOnlyList<RegistroDaExpedicaoDto>>();

    var livro = await _execucao.ListarMovimentacoesDoNoAsync(pecaId, ct);
    var afericoes = await _expedicao.ListarAfericoesAsync([pecaId], ct);
    var contagens = await _expedicao.ListarContagensAsync([pecaId], ct);
    var liberacoes = await _expedicao.ListarLiberacoesAsync([pecaId], ct);

    // Estornada: alguma linha ORIGINAL do registro tem estorno apontando (o estorno de um registro estorna todas as
    // linhas dele numa transacao); a afericao nao tem linha, so o proprio estorno em `RelatorioDimensional`.
    var linhasEstornadas = LiberacaoUseCase.LinhasEstornadas(livro);
    var afericoesEstornadas = afericoes
        .Where(a => a.Tipo == TiposDeAfericao.Estorno && a.EstornoDeId is not null)
        .Select(a => a.EstornoDeId!.Value).ToHashSet();
    bool Estornado(IEnumerable<Movimentacao> daqui) =>
        daqui.Any(l => l.Tipo != TiposDeMovimentacao.Estorno && linhasEstornadas.Contains(l.Id));

    var originais = livro.Where(l => l.Tipo != TiposDeMovimentacao.Estorno).ToList();
    var porContagem = originais.Where(l => l.ContagemId is not null).ToLookup(l => l.ContagemId!.Value);
    var porLiberacao = originais.Where(l => l.LiberacaoId is not null).ToLookup(l => l.LiberacaoId!.Value);
    var porRemessa = originais
        .Where(l => l.Tipo == TiposDeMovimentacao.Expedicao && l.RemessaId is not null)
        .ToLookup(l => l.RemessaId!.Value);
    var consertos = originais.Where(l => l.Tipo == TiposDeMovimentacao.Conserto).ToList();

    var autores = await _execucao.ListarNomesDeUsuariosAsync(
        afericoes.Select(a => a.UsuarioId).Concat(contagens.Select(c => c.UsuarioId))
            .Concat(liberacoes.Select(l => l.UsuarioId)).Concat(originais.Select(l => l.UsuarioId))
            .Distinct().ToList(), ct);
    var setorIds = consertos.Where(l => l.DestinoSetorId is not null).Select(l => l.DestinoSetorId!.Value).Distinct().ToList();
    var setores = (await _catalogo.ObterSetoresPorIdAsync(setorIds, ct)).ToDictionary(s => s.Id, s => s.Nome);
    string Autor(int id) => autores.GetValueOrDefault(id, string.Empty);

    var registros = new List<RegistroDaExpedicaoDto>();
    foreach (var a in afericoes.Where(a => a.Tipo == TiposDeAfericao.Afericao))
    {
      var estornada = afericoesEstornadas.Contains(a.Id);
      registros.Add(new RegistroDaExpedicaoDto(
          TiposDeRegistroDaExpedicao.Afericao, a.Id, a.DataHora, a.UsuarioId, Autor(a.UsuarioId), a.Resultado, null, null,
          null, null, null, estornada, Estornavel(quem, a.UsuarioId, estornada)));
    }
    foreach (var c in contagens)
    {
      var estornada = Estornado(porContagem[c.Id]);
      registros.Add(new RegistroDaExpedicaoDto(
          TiposDeRegistroDaExpedicao.Contagem, c.Id, c.DataHora, c.UsuarioId, Autor(c.UsuarioId), null, c.Conformes,
          c.NaoConformes, null, null, null, estornada, Estornavel(quem, c.UsuarioId, estornada)));
    }
    foreach (var l in consertos)
    {
      var estornada = Estornado([l]);
      registros.Add(new RegistroDaExpedicaoDto(
          TiposDeRegistroDaExpedicao.Conserto, l.Id, l.DataHora, l.UsuarioId, Autor(l.UsuarioId), null, null, null,
          l.Quantidade, l.DestinoSetorId is int s ? setores.GetValueOrDefault(s, string.Empty) : null, null, estornada,
          Estornavel(quem, l.UsuarioId, estornada)));
    }
    foreach (var l in liberacoes)
    {
      var estornada = Estornado(porLiberacao[l.Id]);
      registros.Add(new RegistroDaExpedicaoDto(
          l.Tipo == TiposDeLiberacao.Pedido ? TiposDeRegistroDaExpedicao.PedidoDeLiberacao : TiposDeRegistroDaExpedicao.RespostaDeLiberacao,
          l.Id, l.DataHora, l.UsuarioId, Autor(l.UsuarioId), null, null, null, l.Quantidade, null, l.Texto, estornada,
          Estornavel(quem, l.UsuarioId, estornada)));
    }
    foreach (var remessa in porRemessa)
    {
      var primeira = remessa.OrderBy(l => l.Id).First();
      registros.Add(new RegistroDaExpedicaoDto(
          TiposDeRegistroDaExpedicao.Remessa, remessa.Key, primeira.DataHora, primeira.UsuarioId, Autor(primeira.UsuarioId),
          null, null, null, remessa.Sum(l => l.Quantidade), null, null, Estornado(remessa), Estornavel: false));
    }

    IReadOnlyList<RegistroDaExpedicaoDto> ordenados = registros
        .OrderByDescending(r => r.DataHora).ThenByDescending(r => r.Id).ThenBy(r => r.Tipo, StringComparer.Ordinal)
        .ToList();
    return Result<IReadOnlyList<RegistroDaExpedicaoDto>>.Ok(ordenados);
  }

  // ---------------------------------------------------------------- pendentes

  private async Task<Result<IReadOnlyList<LiberacaoPendenteDto>>> LiberacoesPendentesAsync(CancellationToken ct)
  {
    var pedidos = await _expedicao.ListarPedidosDeLiberacaoAsync(ct);
    if (pedidos.Count == 0) return Result<IReadOnlyList<LiberacaoPendenteDto>>.Ok([]);

    var pecaIds = pedidos.Select(p => p.EstruturaItemId).Distinct().ToList();
    var registros = await _expedicao.ListarLiberacoesAsync(pecaIds, ct);
    var linhas = await _expedicao.ListarLinhasDasLiberacoesAsync(registros.Select(r => r.Id).ToList(), ct);
    var estornadas = LiberacaoUseCase.LinhasEstornadas(linhas);
    var porRegistro = linhas.ToLookup(l => l.LiberacaoId!.Value);
    var respostas = registros.Where(r => r.Tipo == TiposDeLiberacao.Resposta).ToList();
    var pendentes = pedidos.Where(p => LiberacaoUseCase.EstaPendente(p, respostas, estornadas, porRegistro)).ToList();
    if (pendentes.Count == 0) return Result<IReadOnlyList<LiberacaoPendenteDto>>.Ok([]);

    var pendentesIds = pendentes.Select(p => p.EstruturaItemId).ToHashSet();
    var contexto = (await _execucao.ListarNosEmProducaoAsync(ct)).Where(x => pendentesIds.Contains(x.No.Id)).ToDictionary(x => x.No.Id);
    var estado = await _leitor.CarregarAsync(contexto.Values.Select(x => x.No).ToList(), ct);
    var autores = await _execucao.ListarNomesDeUsuariosAsync(pendentes.Select(p => p.UsuarioId).Distinct().ToList(), ct);
    var hoje = PrazoDeEntrega.HojeEmBrasilia(_relogio);

    IReadOnlyList<LiberacaoPendenteDto> lista = pendentes
        .Where(p => contexto.ContainsKey(p.EstruturaItemId))
        .OrderBy(p => p.DataHora).ThenBy(p => p.Id)
        .Select(p =>
        {
          var x = contexto[p.EstruturaItemId];
          return new LiberacaoPendenteDto(
              p.Id, p.Quantidade, p.Texto ?? string.Empty, p.DataHora, autores.GetValueOrDefault(p.UsuarioId, string.Empty),
              new PecaDaLiberacaoDto(x.No.Id, estado.Nome(x.No.Id), estado.Codigos.GetValueOrDefault(x.No.Id)),
              new PedidoDaLiberacaoDto(
                  x.PedidoId, x.PedidoNumero, x.PedidoCliente, x.PedidoDataEntrega,
                  PrazoDeEntrega.EstaAtrasado(x.PedidoDataEntrega, x.PedidoStatus, hoje)));
        })
        .ToList();
    return Result<IReadOnlyList<LiberacaoPendenteDto>>.Ok(lista);
  }

  /// <summary>Estornavel para quem le (C7): ainda sem estorno, e do proprio leitor ou de quem ve os registros de todos.</summary>
  private static bool Estornavel(QuemLe quem, int autorId, bool estornada) =>
      !estornada && (quem.VeRegistrosDeTodos || autorId == quem.UsuarioId);
}

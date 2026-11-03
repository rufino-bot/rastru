using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Expedicao;

/// <summary>
/// Os estornos da afericao, da contagem e da liberacao (pedido ou resposta): C7, C17 e as secoes 3.4 e 3.6 da spec
/// da Fase 5A. Cada registro se estorna INTEIRO, numa transacao (a regra de "pendente" da liberacao supoe isso).
/// Todos seguem o molde de `EstornoUseCase.EstornarMontagem`: o autor (ou PCP/Administrador) fora da transacao, e
/// dentro dela a trava da Peca, o Pedido fechado, o "ja estornado" relido DEPOIS da trava (dois estornos do mesmo
/// registro travam a mesma Peca, e o segundo ve o primeiro), o que impede o estorno e, quando ele move
/// quantidade, o saldo de cada destino original e o recalculo do fechamento. A remessa tem o estorno dela em
/// <see cref="RemessaUseCase.Estornar"/>.
/// </summary>
public sealed class EstornoDaExpedicaoUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly IExpedicaoRepository _expedicao;
  private readonly LeitorDeEstado _leitor;
  private readonly ProjetorDoLivro _projetor;
  private readonly FechamentoDoPedido _fechamento;

  public EstornoDaExpedicaoUseCase(
      IExecucaoRepository execucao, IExpedicaoRepository expedicao, IEstruturaRepository estruturas,
      IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _expedicao = expedicao;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _projetor = new ProjetorDoLivro(execucao, catalogo);
    _fechamento = new FechamentoDoPedido(execucao);
  }

  /// <summary>
  /// Estorna a afericao: grava uma linha `Estorno` em `RelatorioDimensional` apontando-a. Ordem: afericao (404),
  /// autor ou PCP/Administrador (403), estorno de estorno (409 `EstornoImpossivel`); na transacao, Pedido fechado,
  /// ja estornada (409) e, para a nao conforme, contagem valida da Peca (409 `EstornoImpossivel`: as contagens
  /// dependem dela). A afericao nao move quantidade nem muda o fechamento (C2), entao o Pedido so e lido, como em
  /// `AfericaoUseCase.Aferir`. Devolve a afericao original, ja estornada.
  /// </summary>
  public async Task<Result<AfericaoDto>> EstornarAfericao(
      int afericaoId, int usuarioId, bool podeEstornarDeOutros, CancellationToken ct)
  {
    var original = await _expedicao.ObterAfericaoAsync(afericaoId, ct);
    if (original is null) return Falhas.NaoEncontrado<AfericaoDto>();
    if (original.UsuarioId != usuarioId && !podeEstornarDeOutros) return Falhas.Proibido<AfericaoDto>();
    if (original.Tipo == TiposDeAfericao.Estorno)
      return Falhas.Conflito<AfericaoDto>(CodigosDaExecucao.EstornoImpossivel,
          "Estorno não se estorna: registre a aferição de novo.");

    var pecaId = original.EstruturaItemId;
    return await _execucao.ExecutarAsync(async () =>
    {
      var (peca, recusa) = await TravaDaPeca.TravarAsync<AfericaoDto>(_execucao, _leitor, pecaId, travarPedido: false, ct);
      if (recusa is not null) return recusa;

      // Relido DEPOIS da trava.
      if ((await _expedicao.ListarAfericoesEstornadasAsync([afericaoId], ct)).Count > 0)
        return Falhas.Conflito<AfericaoDto>(CodigosDaExecucao.JaEstornado, "Esta aferição já foi estornada.");

      if (original.Resultado == ResultadosDeAfericao.NaoConforme && await TemContagemValidaAsync(pecaId, ct))
      {
        var nome = (await _leitor.CarregarAsync([peca!.No], ct)).Nome(pecaId);
        return Falhas.Conflito<AfericaoDto>(CodigosDaExecucao.EstornoImpossivel,
            $"{nome} já foi contada com base nesta aferição não conforme. Estorne primeiro a contagem.");
      }

      _expedicao.Adicionar(new RelatorioDimensional
      {
        EstruturaItemId = pecaId, Tipo = TiposDeAfericao.Estorno, EstornoDeId = afericaoId,
        DataHora = DateTime.UtcNow, UsuarioId = usuarioId,
      });
      await _execucao.SalvarAlteracoesAsync(ct);

      var autores = await _execucao.ListarNomesDeUsuariosAsync([original.UsuarioId], ct);
      return Result<AfericaoDto>.Ok(new AfericaoDto(
          original.Id, original.Resultado!, original.DataHora, original.UsuarioId,
          autores.GetValueOrDefault(original.UsuarioId, string.Empty), Estornada: true, Estornavel: false));
    }, ct);
  }

  /// <summary>
  /// Estorna a contagem inteira (C17): um `Estorno` por linha `Medicao` dela, de volta a `NaExpedicao`, todos
  /// apontando a mesma contagem. Recusa (409 `EstornoImpossivel`) quando o que ela mediu ja andou (a nao conforme
  /// foi para conserto, a conforme foi expedida...).
  /// </summary>
  public async Task<Result<IReadOnlyList<MovimentacaoDto>>> EstornarContagem(
      int contagemId, int usuarioId, bool podeEstornarDeOutros, CancellationToken ct)
  {
    var contagem = await _expedicao.ObterContagemAsync(contagemId, ct);
    if (contagem is null) return Falhas.NaoEncontrado<IReadOnlyList<MovimentacaoDto>>();
    if (contagem.UsuarioId != usuarioId && !podeEstornarDeOutros) return Falhas.Proibido<IReadOnlyList<MovimentacaoDto>>();

    return await _execucao.ExecutarAsync(async () =>
    {
      var (peca, recusa) = await TravaDaPeca.TravarAsync<IReadOnlyList<MovimentacaoDto>>(
          _execucao, _leitor, contagem.EstruturaItemId, travarPedido: true, ct);
      if (recusa is not null) return recusa;

      var originais = (await _expedicao.ListarLinhasDasContagensAsync([contagemId], ct))
          .Where(l => l.Tipo != TiposDeMovimentacao.Estorno).ToList();
      // Relido DEPOIS da trava: dois estornos simultaneos da mesma contagem travam a mesma Peca, e o segundo ve o
      // primeiro aqui (`UX_Movimentacao_EstornoDe` e a segunda rede).
      if ((await _execucao.ListarEstornadasAsync(originais.Select(l => l.Id).ToList(), ct)).Count > 0)
        return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.JaEstornado, "Esta contagem já foi estornada.");
      return await EstornarLinhasAsync(
          peca!, originais, "Esta contagem", agrupar: e => e.ContagemId = contagemId, usuarioId, ct);
    }, ct);
  }

  /// <summary>
  /// Estorna o pedido de liberacao ou a resposta, inteiro. O pedido com resposta valida e recusado ("estorne a
  /// resposta", 409 `EstornoImpossivel`); a resposta, quando outro pedido da Peca ja esta em aberto (estornar a
  /// resposta reabre o dela, e a Peca teria dois: C18). Em ambos, o que o registro moveu tem de ainda estar onde
  /// ele pos.
  /// </summary>
  public async Task<Result<IReadOnlyList<MovimentacaoDto>>> EstornarLiberacao(
      int liberacaoId, int usuarioId, bool podeEstornarDeOutros, CancellationToken ct)
  {
    var liberacao = await _expedicao.ObterLiberacaoAsync(liberacaoId, ct);
    if (liberacao is null) return Falhas.NaoEncontrado<IReadOnlyList<MovimentacaoDto>>();
    if (liberacao.UsuarioId != usuarioId && !podeEstornarDeOutros) return Falhas.Proibido<IReadOnlyList<MovimentacaoDto>>();

    var pecaId = liberacao.EstruturaItemId;
    return await _execucao.ExecutarAsync(async () =>
    {
      var (peca, recusa) = await TravaDaPeca.TravarAsync<IReadOnlyList<MovimentacaoDto>>(
          _execucao, _leitor, pecaId, travarPedido: true, ct);
      if (recusa is not null) return recusa;

      // Tudo relido DEPOIS da trava da Peca: e ela que serializa o pedido, a resposta e o estorno do mesmo pedido.
      var registros = await _expedicao.ListarLiberacoesAsync([pecaId], ct);
      var linhas = await _expedicao.ListarLinhasDasLiberacoesAsync(registros.Select(r => r.Id).ToList(), ct);
      var estornadas = LiberacaoUseCase.LinhasEstornadas(linhas);
      var porRegistro = linhas.ToLookup(l => l.LiberacaoId!.Value);
      var respostas = registros.Where(r => r.Tipo == TiposDeLiberacao.Resposta).ToList();
      var originais = porRegistro[liberacaoId].Where(l => l.Tipo != TiposDeMovimentacao.Estorno).ToList();

      // O "ja estornado" vem antes das outras recusas: um registro estornado nao tem mais o que impedir.
      if (originais.Any(l => estornadas.Contains(l.Id)))
        return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.JaEstornado,
            liberacao.Tipo == TiposDeLiberacao.Pedido ? "Este pedido já foi estornado." : "Esta resposta já foi estornada.");

      var nome = (await _leitor.CarregarAsync([peca!.No], ct)).Nome(pecaId);
      if (liberacao.Tipo == TiposDeLiberacao.Pedido
          && LiberacaoUseCase.RespostaValida(liberacao, respostas, estornadas, porRegistro) is not null)
        return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.EstornoImpossivel,
            $"O pedido de liberação de {nome} já tem resposta da Gestão: estorne a resposta primeiro.");
      if (liberacao.Tipo == TiposDeLiberacao.Resposta
          && registros.FirstOrDefault(r => r.Id != liberacao.PedidoDeLiberacaoId
              && LiberacaoUseCase.EstaPendente(r, respostas, estornadas, porRegistro)) is { } emAberto)
        return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.EstornoImpossivel,
            $"{nome} já tem um pedido de liberação de {Quantidades.Formatar(emAberto.Quantidade)} em aberto: "
            + "estornar esta resposta reabriria o pedido dela, e só um fica em aberto por vez. Estorne ou responda o outro primeiro.");

      return await EstornarLinhasAsync(
          peca, originais, liberacao.Tipo == TiposDeLiberacao.Pedido ? "Este pedido" : "Esta resposta",
          agrupar: e => e.LiberacaoId = liberacaoId, usuarioId, ct);
    }, ct);
  }

  /// <summary>
  /// O miolo comum da contagem e da liberacao, ja com a Peca e o Pedido travados e o "ja estornado" checado: o saldo de cada destino
  /// original tem de comportar o que volta, e o estorno de cada linha leva a quantidade de volta a origem dela,
  /// apontando a original e o MESMO agrupador (o esquema nao o obriga, mas `ListarLinhasDas*` e a regra de
  /// "pendente" dependem disso). Recalcula o fechamento no fim.
  /// </summary>
  private async Task<Result<IReadOnlyList<MovimentacaoDto>>> EstornarLinhasAsync(
      PecaTravada peca, IReadOnlyList<Movimentacao> originais, string registro, Action<Movimentacao> agrupar,
      int usuarioId, CancellationToken ct)
  {
    var pecaId = peca.No.Id;
    var estado = await _leitor.CarregarAsync([peca.No], ct);
    foreach (var destino in originais.GroupBy(Local.DoDestino))
    {
      var posto = destino.Sum(l => l.Quantidade);
      var ali = estado.Calc.Saldo(pecaId, destino.Key);
      if (posto > ali)
        return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.EstornoImpossivel,
            $"Só há {Quantidades.Formatar(Math.Max(0m, ali))} de {estado.Nome(pecaId)} onde {registro.ToLowerInvariant()} "
            + $"pôs {Quantidades.Formatar(posto)}: a quantidade já andou.");
    }

    var agora = DateTime.UtcNow;
    var estornos = originais.Select(l =>
    {
      var estorno = NovoMovimento.De(l.EstruturaItemId, TiposDeMovimentacao.Estorno, l.Quantidade,
          Local.DoDestino(l), Local.DaOrigem(l), usuarioId, estornoDeId: l.Id);
      agrupar(estorno);
      estorno.DataHora = agora;
      return estorno;
    }).ToList();
    foreach (var estorno in estornos) _execucao.Adicionar(estorno);
    await _execucao.SalvarAlteracoesAsync(ct);
    await _fechamento.RecalcularAsync(peca.Pedidos, agora, ct);
    return Result<IReadOnlyList<MovimentacaoDto>>.Ok(await _projetor.ProjetarMovimentacoesAsync(estornos, ct));
  }

  /// <summary>A Peca tem contagem com alguma linha `Medicao` nao estornada (so a Peca de afericao nao conforme tem).</summary>
  private async Task<bool> TemContagemValidaAsync(int pecaId, CancellationToken ct)
  {
    var contagens = await _expedicao.ListarContagensAsync([pecaId], ct);
    if (contagens.Count == 0) return false;
    var linhas = await _expedicao.ListarLinhasDasContagensAsync(contagens.Select(c => c.Id).ToList(), ct);
    var estornadas = LiberacaoUseCase.LinhasEstornadas(linhas);
    return linhas.Any(l => l.Tipo != TiposDeMovimentacao.Estorno && !estornadas.Contains(l.Id));
  }
}

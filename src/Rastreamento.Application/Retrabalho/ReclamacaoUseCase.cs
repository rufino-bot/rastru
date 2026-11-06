using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Retrabalho;

/// <summary>
/// A reclamacao do cliente (D4, D5 e C13 da spec da Fase 5B-2, secoes 3.3 e 4.7): quantas unidades de uma Peca
/// expedida voltaram, e o que o cliente disse. Fica no Pedido original, em qualquer status dele, e nao mexe no livro
/// (C9). Registrar trava o no e o Pedido (spec da Fase 3, secao 8.1; decisao P5 do plano de backend da Fase 5B-2) e
/// le o expedido e o ja reclamado DEPOIS da trava: e o que serializa duas reclamacoes da mesma Peca e uma
/// reclamacao contra o estorno de uma remessa. O estorno marca o registro (so de inclusao) e e do autor, ou do PCP e
/// do Administrador.
/// </summary>
public sealed class ReclamacaoUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly IReposicaoRepository _reposicao;
  private readonly IReceitaPadraoRepository _catalogo;
  private readonly LeitorDeEstado _leitor;
  private readonly ConsultaDeReposicao _consulta;

  public ReclamacaoUseCase(
      IExecucaoRepository execucao, IReposicaoRepository reposicao, IEstruturaRepository estruturas,
      IReceitaPadraoRepository catalogo, ConsultaDeReposicao consulta)
  {
    _execucao = execucao;
    _reposicao = reposicao;
    _catalogo = catalogo;
    _consulta = consulta;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
  }

  /// <summary>
  /// Recusas do corpo antes da transacao: quantidade, descricao obrigatoria (aparada) e o teto de 500. Dentro dela,
  /// as travas, o no existir e viver no Pedido (404), ser Peca (400 `NaoEhPeca`) e caber no expedido menos o ja
  /// reclamado (409 `SaldoInsuficiente`). O status do Pedido nao recusa (C9).
  /// </summary>
  public async Task<Result<ReclamacaoDto>> Registrar(
      int pedidoId, NovaReclamacaoDto dto, int usuarioId, CancellationToken ct)
  {
    if (!Quantidades.CabeNaColuna(dto.Quantidade)) return Falhas.QuantidadeInvalida<ReclamacaoDto>(dto.Quantidade);
    var texto = dto.Descricao?.Trim();
    if (string.IsNullOrEmpty(texto))
      return Falhas.Validacao<ReclamacaoDto>(CodigosDoRetrabalho.DescricaoObrigatoria,
          "Diga o que o cliente disse sobre a Peça.");
    if (texto.Length > CodigosDaExpedicao.TamanhoMaximoDoTexto)
      return Falhas.Validacao<ReclamacaoDto>(CodigosDaExpedicao.TextoLongoDemais,
          $"O texto tem no máximo {CodigosDaExpedicao.TamanhoMaximoDoTexto} caracteres.");

    return await _execucao.ExecutarAsync(async () =>
    {
      var no = (await _execucao.TravarNosAsync([dto.EstruturaItemId], ct)).SingleOrDefault();
      if (no is null) return Falhas.NaoEncontrado<ReclamacaoDto>();
      if (await _execucao.TravarPedidoAsync(pedidoId, ct) is null) return Falhas.NaoEncontrado<ReclamacaoDto>();
      var estado = await _leitor.CarregarAsync([no], ct);
      var nome = estado.Nome(no.Id);

      if (no.EstruturaPaiId is not null)
        return Falhas.Validacao<ReclamacaoDto>(CodigosDaExpedicao.NaoEhPeca,
            $"{nome} é um Item: só a Peça expedida se reclama.");
      if ((await _execucao.ObterPedidoDoNoAsync(no.Id, ct))?.PedidoId != pedidoId)
        return Falhas.NaoEncontrado<ReclamacaoDto>();

      // Relidos DEPOIS da trava: o expedido liquido (as remessas e os estornos delas) e o ja reclamado.
      var expedido = CalculoDaExpedicao.Posicoes(
          (await _execucao.ListarSaldosAsync([no.Id], ct)).Select(s => (new Local(s.Posicao, s.SetorId, s.Ordem), s.Quantidade)))
          .Expedido;
      var reclamado = await _reposicao.SomarReclamadoAsync(no.Id, ct);
      if (dto.Quantidade > expedido - reclamado)
        return Falhas.Conflito<ReclamacaoDto>(CodigosDaExecucao.SaldoInsuficiente,
            $"Só dá para reclamar mais {Quantidades.Formatar(Math.Max(0m, expedido - reclamado))} de {nome}: "
            + $"foram expedidas {Quantidades.Formatar(expedido)} e já há {Quantidades.Formatar(reclamado)} reclamadas.");

      var reclamacao = new Reclamacao
      {
        PedidoId = pedidoId, EstruturaItemId = no.Id, Quantidade = dto.Quantidade, Descricao = texto,
        DataHora = DateTime.UtcNow, UsuarioId = usuarioId,
      };
      _reposicao.Adicionar(reclamacao);
      await _execucao.SalvarAlteracoesAsync(ct);

      return Result<ReclamacaoDto>.Ok(
          (await ProjetarAsync([reclamacao], new QuemLe(usuarioId, false), ct)).Single());
    }, ct);
  }

  /// <summary>As reclamacoes do Pedido, mais recente primeiro. 404 se o Pedido nao existe.</summary>
  public Task<Result<IReadOnlyList<ReclamacaoDto>>> Listar(int pedidoId, QuemLe quem, CancellationToken ct) =>
      _execucao.ConsultarAsync(async () =>
      {
        if (!await _reposicao.PedidoExisteAsync(pedidoId, ct)) return Falhas.NaoEncontrado<IReadOnlyList<ReclamacaoDto>>();
        var reclamacoes = await _reposicao.ListarReclamacoesAsync(pedidoId, ct);
        return Result<IReadOnlyList<ReclamacaoDto>>.Ok(await ProjetarAsync(reclamacoes, quem, ct));
      }, ct);

  /// <summary>
  /// Estorna a reclamacao. Recusas, nesta ordem: inexistente (404), de outro autor sem ser PCP ou Administrador (403
  /// `Proibido`, decidido aqui com <paramref name="podeEstornarDeOutros"/>) e ja estornada (409 `JaEstornado`, relida
  /// DEPOIS da trava do Pedido) e ja resolvida, com Retrabalho ou com "nao repor" vigente (409 `ReposicaoResolvida`, secao
  /// 3.3 da spec da Fase 5B-2; tambem relida depois da trava, que e a do Pedido que a abertura e o "nao repor" travam).
  /// Devolve a reclamacao ja estornada.
  /// </summary>
  public async Task<Result<ReclamacaoDto>> Estornar(
      int id, int usuarioId, bool podeEstornarDeOutros, CancellationToken ct)
  {
    var lida = await _reposicao.ObterReclamacaoAsync(id, ct);
    if (lida is null) return Falhas.NaoEncontrado<ReclamacaoDto>();
    if (lida.UsuarioId != usuarioId && !podeEstornarDeOutros) return Falhas.Proibido<ReclamacaoDto>();

    return await _execucao.ExecutarAsync(async () =>
    {
      await _execucao.TravarNosAsync([lida.EstruturaItemId], ct);
      await _execucao.TravarPedidoAsync(lida.PedidoId, ct);
      var atual = await _reposicao.ObterReclamacaoAsync(id, ct);   // relida depois da trava
      if (atual is null) return Falhas.NaoEncontrado<ReclamacaoDto>();
      if (atual.EstornadaEm is not null)
        return Falhas.Conflito<ReclamacaoDto>(CodigosDaExecucao.JaEstornado, "Esta reclamação já foi estornada.");

      if (atual.PedidoRetrabalhoId is not null
          || (await _reposicao.ListarDispensasVigentesAsync([], [id], ct)).Count > 0)
        return Falhas.Conflito<ReclamacaoDto>(CodigosDoRetrabalho.ReposicaoResolvida,
            "Esta reclamação já tem Retrabalho ou foi dispensada de repor: desfaça o “não repor” antes de estornar.");

      if (!await _reposicao.MarcarReclamacaoEstornadaAsync(id, usuarioId, DateTime.UtcNow, ct))
        return Falhas.Conflito<ReclamacaoDto>(CodigosDaExecucao.JaEstornado, "Esta reclamação já foi estornada.");
      var estornada = (await _reposicao.ObterReclamacaoAsync(id, ct))!;
      return Result<ReclamacaoDto>.Ok(
          (await ProjetarAsync([estornada], new QuemLe(usuarioId, podeEstornarDeOutros), ct)).Single());
    }, ct);
  }

  /// <summary>
  /// As reclamacoes como DTO, na ordem recebida, com consultas em lote. `PedidoRetrabalho`, `Dispensa` e `ARepor` vem de
  /// `ConsultaDeReposicao` (a quantidade enquanto vigente e sem Retrabalho nem dispensa).
  /// </summary>
  private async Task<IReadOnlyList<ReclamacaoDto>> ProjetarAsync(
      IReadOnlyList<Reclamacao> reclamacoes, QuemLe quem, CancellationToken ct)
  {
    if (reclamacoes.Count == 0) return [];
    var nos = (await _execucao.ListarNosAsync(reclamacoes.Select(r => r.EstruturaItemId).Distinct().ToList(), ct))
        .ToDictionary(n => n.Id);
    var componenteIds = nos.Values.Where(n => n.ComponenteId is not null).Select(n => n.ComponenteId!.Value).Distinct().ToList();
    var componentes = (await _catalogo.ObterComponentesPorIdAsync(componenteIds, ct)).ToDictionary(c => c.Id);
    var usuarios = await _execucao.ListarNomesDeUsuariosAsync(reclamacoes.Select(r => r.UsuarioId).Distinct().ToList(), ct);
    var reposicao = await _consulta.PorReclamacao(reclamacoes, ct);

    return reclamacoes.Select(r =>
    {
      var no = nos.GetValueOrDefault(r.EstruturaItemId);
      var componente = no?.ComponenteId is int c ? componentes.GetValueOrDefault(c) : null;
      var estornada = r.EstornadaEm is not null;
      return new ReclamacaoDto(
          r.Id, r.EstruturaItemId,
          // A descricao pela regra 19 (a do no, senao a do Componente), como `EstadoDeExecucao.Nome`.
          no?.Descricao ?? componente?.Descricao ?? string.Empty, componente?.Codigo, r.Quantidade, r.Descricao,
          r.DataHora, r.UsuarioId, usuarios.GetValueOrDefault(r.UsuarioId, string.Empty), estornada,
          !estornada && (quem.VeRegistrosDeTodos || r.UsuarioId == quem.UsuarioId),
          reposicao[r.Id].PedidoRetrabalho, reposicao[r.Id].Dispensa, reposicao[r.Id].ARepor);
    }).ToList();
  }
}

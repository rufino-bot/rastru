using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Baixa;

/// <summary>
/// O "Descartar tudo" do Agrupamento e do Pedido (secoes 4.8 e 5.1 da spec da Fase 5B-1, P6 do plano de backend da
/// Fase 5B-1): a mesma gravacao, a mesma previa e a mesma conta da baixa de um no, sobre a floresta das Pecas do escopo.
/// </summary>
public sealed partial class BaixaUseCase
{
  /// <summary>
  /// O "Descartar tudo" do Agrupamento (P6 do plano de backend da Fase 5B-1): uma baixa de tudo, motivo `Descarte`, de
  /// cada Peca do Agrupamento que tem algo em producao, todas na mesma `Perda`, com o `AgrupamentoId`. 404 se o
  /// Agrupamento nao existe.
  /// </summary>
  public Task<Result<BaixaDto>> DescartarAgrupamento(int agrupamentoId, DescarteDto dto, int usuarioId, CancellationToken ct) =>
      DescartarAsync(agrupamentoId, null, dto, usuarioId, ct);

  /// <summary>O "Descartar tudo" do Pedido: como o do Agrupamento, sobre todos os do Pedido, sem `AgrupamentoId`.</summary>
  public Task<Result<BaixaDto>> DescartarPedido(int pedidoId, DescarteDto dto, int usuarioId, CancellationToken ct) =>
      DescartarAsync(null, pedidoId, dto, usuarioId, ct);

  /// <summary>O que o "Descartar tudo" do Agrupamento gravaria, sem travar nem gravar (C14).</summary>
  public Task<Result<PreviaDaBaixaDto>> PreverDescarteDoAgrupamento(int agrupamentoId, CancellationToken ct) =>
      PreverDescarteAsync(agrupamentoId, null, ct);

  /// <summary>O que o "Descartar tudo" do Pedido gravaria, sem travar nem gravar (C14).</summary>
  public Task<Result<PreviaDaBaixaDto>> PreverDescarteDoPedido(int pedidoId, CancellationToken ct) =>
      PreverDescarteAsync(null, pedidoId, ct);

  private async Task<Result<BaixaDto>> DescartarAsync(
      int? agrupamentoId, int? pedidoId, DescarteDto dto, int usuarioId, CancellationToken ct)
  {
    var validada = ValidarObservacao(dto.Observacao);
    if (!validada.Sucesso) return Falhas.Repassar<BaixaDto, string?>(validada);
    var observacao = validada.Valor;

    return await _execucao.ExecutarAsync(async () =>
    {
      var planejada = await PlanejarDescarteAsync(agrupamentoId, pedidoId, travar: true, ct);
      if (!planejada.Sucesso) return Falhas.Repassar<BaixaDto, Planejada>(planejada);
      var (plano, _, doPedidoId) = planejada.Valor!;

      var perda = new Perda
      {
        Motivo = MotivosDePerda.Descarte, Observacao = observacao, PedidoId = doPedidoId, AgrupamentoId = agrupamentoId,
        EstruturaItemId = null, Tudo = true,
      };
      return Result<BaixaDto>.Ok(await GravarAsync(perda, plano, usuarioId, ct));
    }, ct);
  }

  private async Task<Result<PreviaDaBaixaDto>> PreverDescarteAsync(int? agrupamentoId, int? pedidoId, CancellationToken ct) =>
      await _execucao.ConsultarAsync(async () =>
      {
        var planejada = await PlanejarDescarteAsync(agrupamentoId, pedidoId, travar: false, ct);
        if (!planejada.Sucesso) return Falhas.Repassar<PreviaDaBaixaDto, Planejada>(planejada);
        var (plano, estado, _) = planejada.Valor!;
        return Result<PreviaDaBaixaDto>.Ok(await PreviaAsync(plano, estado, ct));
      }, ct);

  /// <summary>
  /// O plano do "Descartar tudo": a floresta das Pecas do escopo (as do Agrupamento, ou as de todos os do Pedido),
  /// lida e, na escrita, com os nos travados em ordem crescente de Id e depois o Pedido (spec da Fase 3, secao 8.1). Cada Peca
  /// com algo em producao entra com um `Tudo` (P6 do plano de backend da Fase 5B-1); a que nao tem nada (toda
  /// expedida, montada ou perdida) nao gera linha. Com unidade aguardando liberacao, recusa UMA vez nomeando todas as
  /// Pecas nessa situacao (C15 da spec da Fase 5B-1). So o Pedido `Cancelado` recusa; a pausa nao (C13).
  /// </summary>
  private async Task<Result<Planejada>> PlanejarDescarteAsync(int? agrupamentoId, int? pedidoIdDado, bool travar, CancellationToken ct)
  {
    int pedidoId;
    if (agrupamentoId is int ag)
    {
      if (await _baixas.ObterPedidoDoAgrupamentoAsync(ag, ct) is not int doAgrupamento) return Falhas.NaoEncontrado<Planejada>();
      pedidoId = doAgrupamento;
    }
    else pedidoId = pedidoIdDado!.Value;
    if (!(await _baixas.ListarStatusDosPedidosAsync([pedidoId], ct)).ContainsKey(pedidoId))
      return Falhas.NaoEncontrado<Planejada>();

    var pecas = await _baixas.ListarPecasDoEscopoAsync(pedidoId, agrupamentoId, ct);
    var semNada = Falhas.Conflito<Planejada>(CodigosDaBaixa.NadaEmProducao,
        agrupamentoId is null ? "O Pedido não tem nada em produção para descartar." : "O Agrupamento não tem nada em produção para descartar.");
    if (pecas.Count == 0) return semNada;

    var arvore = new SortedSet<int>();
    foreach (var peca in pecas) foreach (var id in await _baixas.ListarSubarvoreAsync(peca, ct)) arvore.Add(id);

    var nos = travar ? await _execucao.TravarNosAsync(arvore.ToList(), ct) : await _execucao.ListarNosAsync(arvore.ToList(), ct);
    if (travar) await _execucao.TravarPedidosDosNosAsync(pecas, ct);

    var pedido = await _execucao.ObterPedidoDoNoAsync(pecas[0], ct);
    if (pedido is null) return Falhas.NaoEncontrado<Planejada>();
    if (pedido.Status == StatusCancelado)
      return Falhas.Conflito<Planejada>(CodigosDaExecucao.PedidoFechado,
          $"O Pedido {pedido.Numero} foi cancelado: não se dá baixa nele.");

    var estado = await _leitor.CarregarAsync(nos, ct);
    var linhas = new List<LinhaPlanejada>();
    var sobras = new List<(int NoId, decimal Quantidade)>();
    var presas = new List<string>();
    foreach (var peca in pecas)
    {
      var plano = CalculoDaBaixa.Planejar(
          estado.Calc, new PedidoDeBaixa(peca, MotivosDePerda.Descarte, true, [], false, []), estado.Nome, id => $"Setor {id}");
      if (plano.Sucesso)
      {
        linhas.AddRange(plano.Valor!.Linhas);
        sobras.AddRange(plano.Valor.ViramSobra);
      }
      else if (plano.Erro == CodigosDaExpedicao.LiberacaoEmAberto) presas.Add(estado.Nome(peca));
      else if (plano.Erro != CodigosDaBaixa.NadaEmProducao) return Falhas.Repassar<Planejada, PlanoDaBaixa>(plano);
    }
    if (presas.Count > 0)
      return Falhas.Conflito<Planejada>(CodigosDaExpedicao.LiberacaoEmAberto,
          $"Há unidades aguardando liberação em {string.Join(", ", presas)}: "
          + "estorne os pedidos de liberação antes de descartar tudo.");
    if (linhas.Count == 0) return semNada;

    return Result<Planejada>.Ok(new Planejada(new PlanoDaBaixa(linhas, sobras, null), estado, pedidoId));
  }
}

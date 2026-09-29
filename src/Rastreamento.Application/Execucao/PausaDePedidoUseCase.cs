using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// Pausar e retomar um Pedido (spec da Fase 3D, secoes 2.5 e 4.5). A pausa recusa so o Iniciar — os
/// outros registros sao fatos que ja aconteceram no chao — e cada pausa e um intervalo em
/// `dbo.PedidoPausa`, para a Fase 6 poder descontar o tempo pausado.
/// </summary>
public sealed class PausaDePedidoUseCase
{
  public const int TamanhoMaximoDoMotivo = 200;

  private readonly IExecucaoRepository _execucao;

  public PausaDePedidoUseCase(IExecucaoRepository execucao) => _execucao = execucao;

  public async Task<Result<PausaDto>> Pausar(int pedidoId, NovaPausaDto dto, int usuarioId, CancellationToken ct)
  {
    var motivo = dto.Motivo?.Trim();
    if (string.IsNullOrEmpty(motivo)) motivo = null;
    if (motivo is { Length: > TamanhoMaximoDoMotivo })
      return Falhas.Validacao<PausaDto>(CodigosDaExecucao.MotivoLongoDemais,
          $"O motivo tem {motivo.Length} caracteres; o limite é {TamanhoMaximoDoMotivo}.");

    return await _execucao.ExecutarAsync(async () =>
    {
      var pedido = await _execucao.TravarPedidoAsync(pedidoId, ct);
      if (pedido is null) return Falhas.NaoEncontrado<PausaDto>();
      if (pedido.Status is "Concluido" or "Cancelado") return Falhas.PedidoFechado<PausaDto>();
      if (await _execucao.ObterPausaAbertaAsync(pedidoId, ct) is not null)
        return Falhas.Conflito<PausaDto>(CodigosDaExecucao.PedidoJaPausado, $"O Pedido {pedido.Numero} já está pausado.");

      var pausa = new PedidoPausa
      {
        PedidoId = pedidoId, PausadoEm = DateTime.UtcNow, PausadoPorUsuarioId = usuarioId, Motivo = motivo,
      };
      _execucao.Adicionar(pausa);
      await _execucao.SalvarAlteracoesAsync(ct);
      return Result<PausaDto>.Ok(await ProjetarAsync(pausa, ct));
    }, ct);
  }

  public async Task<Result<PausaDto>> Retomar(int pedidoId, int usuarioId, CancellationToken ct) =>
      await _execucao.ExecutarAsync(async () =>
      {
        var pedido = await _execucao.TravarPedidoAsync(pedidoId, ct);
        if (pedido is null) return Falhas.NaoEncontrado<PausaDto>();
        // Retomar um Pedido que fechou com a pausa aberta e permitido: so fecha o intervalo.
        var pausa = await _execucao.ObterPausaAbertaAsync(pedidoId, ct);
        if (pausa is null)
          return Falhas.Conflito<PausaDto>(CodigosDaExecucao.PedidoNaoPausado, $"O Pedido {pedido.Numero} não está pausado.");

        var agora = DateTime.UtcNow;
        await _execucao.FecharPausaAsync(pausa.Id, usuarioId, agora, ct);
        pausa.RetomadoEm = agora;
        pausa.RetomadoPorUsuarioId = usuarioId;
        return Result<PausaDto>.Ok(await ProjetarAsync(pausa, ct));
      }, ct);

  private async Task<PausaDto> ProjetarAsync(PedidoPausa p, CancellationToken ct)
  {
    var ids = new[] { p.PausadoPorUsuarioId }.Concat(p.RetomadoPorUsuarioId is int r ? [r] : []).Distinct().ToList();
    var nomes = await _execucao.ListarNomesDeUsuariosAsync(ids, ct);
    return new PausaDto(p.Id, p.PedidoId, p.PausadoEm, p.PausadoPorUsuarioId,
        nomes.GetValueOrDefault(p.PausadoPorUsuarioId, string.Empty), p.Motivo, p.RetomadoEm, p.RetomadoPorUsuarioId,
        p.RetomadoPorUsuarioId is int quem ? nomes.GetValueOrDefault(quem, string.Empty) : null);
  }
}

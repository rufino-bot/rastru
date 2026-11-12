using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Retrabalho;

/// <summary>
/// O "nao repor" (D3, C3 e C15 da spec da Fase 5B-2, secoes 3.4 e 4.6) e desfaze-lo. A dispensa vale para a baixa de
/// perda ou a reclamacao INTEIRA e so tira o alvo do "a repor": e registro, com justificativa obrigatoria, e desfazer
/// marca o proprio registro (so de inclusao).
///
/// A concorrencia (decisao P5 do plano de backend da Fase 5B-2): como a abertura do Retrabalho, trava os nos do alvo, em
/// ordem crescente de Id, e depois o Pedido dele, e so ENTAO rele o alvo. A abertura e o "nao repor" do mesmo alvo
/// travam o mesmo Pedido: o segundo ve o primeiro e recusa (409 `JaReposto` ou `ReposicaoDispensada`), e nunca os dois
/// valem. O que se le ANTES da transacao (a baixa, as linhas dela, a reclamacao) so serve para saber o que travar.
/// </summary>
public sealed class DispensaUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly IReposicaoRepository _reposicao;
  private readonly IBaixaRepository _baixas;
  private readonly ConsultaDeReposicao _consulta;

  public DispensaUseCase(
      IExecucaoRepository execucao, IReposicaoRepository reposicao, IBaixaRepository baixas, ConsultaDeReposicao consulta)
  {
    _execucao = execucao;
    _reposicao = reposicao;
    _baixas = baixas;
    _consulta = consulta;
  }

  /// <summary>
  /// Dispensa a reposicao da baixa. Recusas do corpo antes da transacao: justificativa obrigatoria (aparada, 400
  /// `JustificativaObrigatoria`) e o teto de 500 (400 `TextoLongoDemais`). Depois: baixa inexistente (404); so a baixa de
  /// `PerdaArmazem` ou `MortaEmProcesso` aceita (409 `SemPecaARepor`). Dentro da transacao, relido depois das travas: ja
  /// ter Retrabalho (409 `JaReposto`), ja ter dispensa vigente (409 `ReposicaoDispensada`), estar estornada e nao ter
  /// levado nenhuma unidade de Peca (409 `SemPecaARepor`, C1).
  /// </summary>
  public async Task<Result<DispensaDto>> DispensarBaixa(
      int perdaId, NovaDispensaDto dto, int usuarioId, CancellationToken ct)
  {
    var justificativa = ConferirJustificativa(dto);
    if (!justificativa.Sucesso) return Falhas.Repassar<DispensaDto, string>(justificativa);

    var lida = await _execucao.ConsultarAsync(async () =>
    {
      var perda = await _baixas.ObterAsync(perdaId, ct);
      if (perda is null) return Falhas.NaoEncontrado<LidaDaBaixa>();
      if (!MotivosDePerda.QueSeRepoem.Contains(perda.Motivo))
        return Falhas.Conflito<LidaDaBaixa>(CodigosDoRetrabalho.SemPecaARepor,
            "Só a baixa de perda se repõe: o descarte é o que saiu do projeto.");
      var nos = (await _baixas.ListarLinhasDasPerdasAsync([perdaId], ct))
          .Where(l => l.Tipo == TiposDeMovimentacao.Perda).Select(l => l.EstruturaItemId).Distinct().Order().ToList();
      return Result<LidaDaBaixa>.Ok(new LidaDaBaixa(perda.PedidoId, nos));
    }, ct);
    if (!lida.Sucesso) return Falhas.Repassar<DispensaDto, LidaDaBaixa>(lida);
    var (pedidoId, noIds) = lida.Valor!;

    return await _execucao.ExecutarAsync(async () =>
    {
      if (noIds.Count > 0) await _execucao.TravarNosAsync(noIds, ct);
      if (await _execucao.TravarPedidoAsync(pedidoId, ct) is null) return Falhas.NaoEncontrado<DispensaDto>();

      // Relida DEPOIS das travas: a abertura do Retrabalho, o "nao repor" e o estorno da baixa travam o mesmo Pedido.
      var perda = await _baixas.ObterAsync(perdaId, ct);
      if (perda is null) return Falhas.NaoEncontrado<DispensaDto>();
      if (perda.PedidoRetrabalhoId is int reposta) return await JaRepostoAsync("Esta baixa", reposta, ct);
      if ((await _reposicao.ListarDispensasVigentesAsync([perdaId], [], ct)).Count > 0) return JaDispensada<DispensaDto>("Esta baixa");

      var linhas = await _baixas.ListarLinhasDasPerdasAsync([perdaId], ct);
      if (linhas.Any(l => l.Tipo == TiposDeMovimentacao.Estorno))
        return Falhas.Conflito<DispensaDto>(CodigosDoRetrabalho.SemPecaARepor, "Esta baixa foi estornada: não há Peça a repor.");
      var pecas = (await _execucao.ListarNosAsync(noIds, ct)).Where(n => n.EstruturaPaiId is null).Select(n => n.Id).ToHashSet();
      if (!linhas.Any(l => l.Tipo == TiposDeMovimentacao.Perda && pecas.Contains(l.EstruturaItemId)))
        return Falhas.Conflito<DispensaDto>(CodigosDoRetrabalho.SemPecaARepor,
            "Esta baixa não levou nenhuma unidade da Peça: não há Peça a repor.");

      return await GravarAsync(new DispensaDeReposicao { PerdaId = perdaId }, justificativa.Valor!, usuarioId, ct);
    }, ct);
  }

  /// <summary>
  /// Dispensa a reposicao da reclamacao. Mesmas recusas do corpo; reclamacao inexistente (404). Relido depois das
  /// travas: ja ter Retrabalho (409 `JaReposto`), ja ter dispensa vigente (409 `ReposicaoDispensada`) e estar estornada
  /// (409 `SemPecaARepor`).
  /// </summary>
  public async Task<Result<DispensaDto>> DispensarReclamacao(
      int reclamacaoId, NovaDispensaDto dto, int usuarioId, CancellationToken ct)
  {
    var justificativa = ConferirJustificativa(dto);
    if (!justificativa.Sucesso) return Falhas.Repassar<DispensaDto, string>(justificativa);

    var lida = await _reposicao.ObterReclamacaoAsync(reclamacaoId, ct);
    if (lida is null) return Falhas.NaoEncontrado<DispensaDto>();

    return await _execucao.ExecutarAsync(async () =>
    {
      await _execucao.TravarNosAsync([lida.EstruturaItemId], ct);
      if (await _execucao.TravarPedidoAsync(lida.PedidoId, ct) is null) return Falhas.NaoEncontrado<DispensaDto>();

      var atual = await _reposicao.ObterReclamacaoAsync(reclamacaoId, ct);
      if (atual is null) return Falhas.NaoEncontrado<DispensaDto>();
      if (atual.PedidoRetrabalhoId is int reposta) return await JaRepostoAsync("Esta reclamação", reposta, ct);
      if ((await _reposicao.ListarDispensasVigentesAsync([], [reclamacaoId], ct)).Count > 0)
        return JaDispensada<DispensaDto>("Esta reclamação");
      if (atual.EstornadaEm is not null)
        return Falhas.Conflito<DispensaDto>(CodigosDoRetrabalho.SemPecaARepor, "Esta reclamação foi estornada: não há Peça a repor.");

      return await GravarAsync(new DispensaDeReposicao { ReclamacaoId = reclamacaoId }, justificativa.Valor!, usuarioId, ct);
    }, ct);
  }

  /// <summary>
  /// Desfaz o "nao repor": marca a dispensa (quem e quando), sem apagar (C15). Dispensa inexistente (404); ja desfeita
  /// (409 `JaEstornado`, relida DEPOIS da trava do Pedido do alvo). Qualquer PCP ou Administrador desfaz, nao so o autor.
  /// </summary>
  public async Task<Result> Estornar(int dispensaId, int usuarioId, CancellationToken ct)
  {
    var lida = await _reposicao.ObterDispensaAsync(dispensaId, ct);
    if (lida is null) return Result.Falha("NaoEncontrado", TipoDeErro.NaoEncontrado);
    var pedidoId = await PedidoDoAlvoAsync(lida, ct);
    if (pedidoId is null) return Result.Falha("NaoEncontrado", TipoDeErro.NaoEncontrado);

    var resultado = await _execucao.ExecutarAsync(async () =>
    {
      if (await _execucao.TravarPedidoAsync(pedidoId.Value, ct) is null) return Falhas.NaoEncontrado<bool>();
      var atual = await _reposicao.ObterDispensaAsync(dispensaId, ct);
      if (atual is null) return Falhas.NaoEncontrado<bool>();
      if (atual.EstornadaEm is not null || !await _reposicao.MarcarDispensaEstornadaAsync(dispensaId, usuarioId, DateTime.UtcNow, ct))
        return Falhas.Conflito<bool>(CodigosDaExecucao.JaEstornado, "Este “não repor” já foi desfeito.");
      return Result<bool>.Ok(true);
    }, ct);
    return resultado.Sucesso ? Result.Ok() : Result.Falha(resultado.Erro!, resultado.TipoDoErro!.Value, resultado.Detalhe);
  }

  private sealed record LidaDaBaixa(int PedidoId, IReadOnlyList<int> NoIds);

  /// <summary>A justificativa aparada, ate 500 caracteres (C15).</summary>
  private static Result<string> ConferirJustificativa(NovaDispensaDto dto)
  {
    var texto = dto.Justificativa?.Trim();
    if (string.IsNullOrEmpty(texto))
      return Falhas.Validacao<string>(CodigosDaExpedicao.JustificativaObrigatoria, "Diga por que não se repõe.");
    if (texto.Length > CodigosDaExpedicao.TamanhoMaximoDoTexto)
      return Falhas.Validacao<string>(CodigosDaExpedicao.TextoLongoDemais,
          $"O texto tem no máximo {CodigosDaExpedicao.TamanhoMaximoDoTexto} caracteres.");
    return Result<string>.Ok(texto);
  }

  private async Task<int?> PedidoDoAlvoAsync(DispensaDeReposicao dispensa, CancellationToken ct) =>
      dispensa.PerdaId is int perda
          ? (await _baixas.ObterAsync(perda, ct))?.PedidoId
          : (await _reposicao.ObterReclamacaoAsync(dispensa.ReclamacaoId!.Value, ct))?.PedidoId;

  private async Task<Result<DispensaDto>> GravarAsync(
      DispensaDeReposicao dispensa, string justificativa, int usuarioId, CancellationToken ct)
  {
    dispensa.Justificativa = justificativa;
    dispensa.DataHora = DateTime.UtcNow;
    dispensa.UsuarioId = usuarioId;
    _reposicao.Adicionar(dispensa);
    await _execucao.SalvarAlteracoesAsync(ct);
    return Result<DispensaDto>.Ok(await _consulta.ProjetarDispensaAsync(dispensa, ct));
  }

  private async Task<Result<DispensaDto>> JaRepostoAsync(string alvo, int retrabalhoId, CancellationToken ct)
  {
    var numero = (await _reposicao.ListarNumerosDePedidosAsync([retrabalhoId], ct)).GetValueOrDefault(retrabalhoId);
    return Falhas.Conflito<DispensaDto>(CodigosDoRetrabalho.JaReposto,
        $"{alvo} já tem o Retrabalho {numero}: não há mais o que dispensar.");
  }

  private static Result<T> JaDispensada<T>(string alvo) =>
      Falhas.Conflito<T>(CodigosDoRetrabalho.ReposicaoDispensada, $"{alvo} já está dispensada de repor.");
}

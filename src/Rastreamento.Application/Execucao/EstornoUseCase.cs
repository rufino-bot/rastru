using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// Correcao do livro, que e so de inclusao: o estorno grava o movimento inverso, apontando o original
/// (spec da Fase 3, secao 4.5). So enquanto a quantidade nao andou — o destino original ainda tem de
/// comportar o que volta. Uma vez so por registro (`UX_Movimentacao_EstornoDe` garante no banco).
/// Estorno nao se estorna; a baixa de filho de uma montagem so sai com a montagem inteira.
/// </summary>
public sealed class EstornoUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly LeitorDeEstado _leitor;
  private readonly ProjetorDoLivro _projetor;

  public EstornoUseCase(IExecucaoRepository execucao, IEstruturaRepository estruturas, IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _projetor = new ProjetorDoLivro(execucao, catalogo);
  }

  public async Task<Result<MovimentacaoDto>> EstornarMovimentacao(
      int movimentacaoId, int usuarioId, bool podeEstornarDeOutros, CancellationToken ct)
  {
    var original = await _execucao.ObterMovimentacaoAsync(movimentacaoId, ct);
    if (original is null) return Falhas.NaoEncontrado<MovimentacaoDto>();
    if (original.UsuarioId != usuarioId && !podeEstornarDeOutros) return Falhas.Proibido<MovimentacaoDto>();
    if (original.Tipo == TiposDeMovimentacao.Estorno)
      return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.EstornoImpossivel,
          "Estorno não se estorna: registre de novo a operação original.");
    if (original.MontagemId is int montagemId)
      return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.EstornoImpossivel,
          $"Esta baixa faz parte da montagem {montagemId}: estorne a montagem inteira.");

    return await _execucao.ExecutarAsync(async () =>
    {
      var nos = await _execucao.TravarNosAsync([original.EstruturaItemId], ct);
      if (nos.Count == 0) return Falhas.NaoEncontrado<MovimentacaoDto>();
      if (Falhas.EstaFechado(await _execucao.ObterPedidoDoNoAsync(original.EstruturaItemId, ct)))
        return Falhas.PedidoFechado<MovimentacaoDto>();
      // Relido DEPOIS da trava: dois estornos simultaneos do mesmo registro travam o mesmo no, e o
      // segundo ve o primeiro aqui.
      if ((await _execucao.ListarEstornadasAsync([original.Id], ct)).Count > 0)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.JaEstornado, "Este registro já foi estornado.");

      var estado = await _leitor.CarregarAsync(nos, ct);
      var destino = Local.DoDestino(original);
      var ali = estado.Calc.Saldo(original.EstruturaItemId, destino);
      if (original.Quantidade > ali)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.EstornoImpossivel,
            $"Só há {Quantidades.Formatar(ali)} de {estado.Nome(original.EstruturaItemId)} onde este registro pôs "
            + $"{Quantidades.Formatar(original.Quantidade)}: a quantidade já andou.");

      var estorno = NovoMovimento.De(original.EstruturaItemId, TiposDeMovimentacao.Estorno, original.Quantidade,
          destino, Local.DaOrigem(original), usuarioId, estornoDeId: original.Id);
      _execucao.Adicionar(estorno);
      await _execucao.SalvarAlteracoesAsync(ct);
      return Result<MovimentacaoDto>.Ok((await _projetor.ProjetarMovimentacoesAsync([estorno], ct)).Single());
    }, ct);
  }

  public async Task<Result<IReadOnlyList<MovimentacaoDto>>> EstornarMontagem(
      int montagemId, int usuarioId, bool podeEstornarDeOutros, CancellationToken ct)
  {
    var montagem = await _execucao.ObterMontagemAsync(montagemId, ct);
    if (montagem is null) return Falhas.NaoEncontrado<IReadOnlyList<MovimentacaoDto>>();
    if (montagem.UsuarioId != usuarioId && !podeEstornarDeOutros) return Falhas.Proibido<IReadOnlyList<MovimentacaoDto>>();

    return await _execucao.ExecutarAsync(async () =>
    {
      var baixas = await _execucao.ListarBaixasAsync([montagemId], ct);
      var nos = await _execucao.TravarNosAsync(baixas.Select(b => b.EstruturaItemId).Append(montagem.EstruturaItemId), ct);
      if (Falhas.EstaFechado(await _execucao.ObterPedidoDoNoAsync(montagem.EstruturaItemId, ct)))
        return Falhas.PedidoFechado<IReadOnlyList<MovimentacaoDto>>();
      var atual = await _execucao.ObterMontagemAsync(montagemId, ct);   // relida depois da trava
      if (atual!.EstornadaEm is not null)
        return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.JaEstornado, "Esta montagem já foi estornada.");

      var estado = await _leitor.CarregarAsync(nos, ct);
      foreach (var baixa in baixas)
        // Cinto de seguranca, e nao alcancavel hoje: o que uma montagem baixou (Montado) so sai por meio
        // do estorno DESTA mesma montagem, e a checagem de `CodigosDaExecucao.JaEstornado` ja bloqueia
        // um segundo estorno dela — entao nao ha caminho para "Montado" cair abaixo do que ela mesma
        // gravou. Sem teste porque nao ha como forcar esta condicao pelos casos de uso publicos.
        if (estado.Calc.Saldo(baixa.EstruturaItemId, Local.Montado) < baixa.Quantidade)
          return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.EstornoImpossivel,
              $"{estado.Nome(baixa.EstruturaItemId)} não tem mais o que esta montagem baixou.");

      var estornos = baixas.Select(b => NovoMovimento.De(b.EstruturaItemId, TiposDeMovimentacao.Estorno, b.Quantidade,
          Local.Montado, Local.DaOrigem(b), usuarioId, montagemId: montagemId, estornoDeId: b.Id)).ToList();
      foreach (var estorno in estornos) _execucao.Adicionar(estorno);
      await _execucao.MarcarMontagemEstornadaAsync(montagemId, usuarioId, DateTime.UtcNow, ct);
      await _execucao.SalvarAlteracoesAsync(ct);
      return Result<IReadOnlyList<MovimentacaoDto>>.Ok(await _projetor.ProjetarMovimentacoesAsync(estornos, ct));
    }, ct);
  }
}

using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Expedicao;

/// <summary>
/// A Qualidade no local de expedicao (D1 da spec da Fase 5A): aferir a Peca (secao 3.4), contar as unidades
/// uma a uma depois de uma afericao nao conforme (D4) e mandar as nao conformes para conserto (D6, C19). Toda
/// escrita corre na transacao da execucao (secao 5.1): trava o no, depois o Pedido quando a escrita mexe no
/// livro (spec da Fase 3, secao 8.1), e so entao le o Pedido e valida. Pedido `Concluido` ou `Cancelado`
/// recusa as tres.
/// </summary>
public sealed class AfericaoUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly IExpedicaoRepository _expedicao;
  private readonly ISetorRepository _setores;
  private readonly LeitorDeEstado _leitor;
  private readonly ProjetorDoLivro _projetor;
  private readonly FechamentoDoPedido _fechamento;

  public AfericaoUseCase(
      IExecucaoRepository execucao, IExpedicaoRepository expedicao, IEstruturaRepository estruturas,
      ISetorRepository setores, IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _expedicao = expedicao;
    _setores = setores;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _projetor = new ProjetorDoLivro(execucao, catalogo);
    _fechamento = new FechamentoDoPedido(execucao);
  }

  public async Task<Result<AfericaoDto>> Aferir(int pecaId, NovaAfericaoDto dto, int usuarioId, CancellationToken ct)
  {
    if (dto.Resultado is not (ResultadosDeAfericao.Conforme or ResultadosDeAfericao.NaoConforme))
      return Falhas.Validacao<AfericaoDto>(CodigosDaExpedicao.ResultadoInvalido,
          $"O resultado da aferição é {ResultadosDeAfericao.Conforme} ou {ResultadosDeAfericao.NaoConforme}.");

    return await _execucao.ExecutarAsync(async () =>
    {
      // A afericao nao move quantidade (C2) e nao muda o fechamento: o Pedido so e lido, nao travado.
      var (peca, recusa) = await TravarAPecaAsync<AfericaoDto>(pecaId, travarPedido: false, ct);
      if (recusa is not null) return recusa;
      var nome = (await _leitor.CarregarAsync([peca!.No], ct)).Nome(pecaId);

      if (!peca.No.RequerRelatorioDimensional)
        return Falhas.Conflito<AfericaoDto>(CodigosDaExpedicao.SemRelatorioDimensional,
            $"{nome} não exige relatório dimensional: não há o que aferir.");

      var valida = CalculoDaExpedicao.AfericaoValida(await _expedicao.ListarAfericoesAsync([pecaId], ct));
      if (valida is not null)
        return Falhas.Conflito<AfericaoDto>(CodigosDaExpedicao.AfericaoJaRegistrada,
            $"{nome} já tem uma aferição {Rotulo(valida.Resultado)}. Para registrar outra, estorne essa primeiro.");

      var afericao = new RelatorioDimensional
      {
        EstruturaItemId = pecaId, Tipo = TiposDeAfericao.Afericao, Resultado = dto.Resultado,
        DataHora = DateTime.UtcNow, UsuarioId = usuarioId,
      };
      _expedicao.Adicionar(afericao);
      await _execucao.SalvarAlteracoesAsync(ct);

      // Recem-registrada: ninguem a estornou, e quem le e o autor (C7).
      return Result<AfericaoDto>.Ok(new AfericaoDto(
          afericao.Id, afericao.Resultado!, afericao.DataHora, usuarioId, await NomeDoUsuarioAsync(usuarioId, ct),
          Estornada: false, Estornavel: true));
    }, ct);
  }

  public async Task<Result<ContagemDto>> Contar(int pecaId, NovaContagemDto dto, int usuarioId, CancellationToken ct)
  {
    foreach (var quantidade in new[] { dto.Conformes, dto.NaoConformes })
      if (quantidade != 0m && !Quantidades.CabeNaColuna(quantidade))
        return Falhas.QuantidadeInvalida<ContagemDto>(quantidade);
    if (dto.Conformes == 0m && dto.NaoConformes == 0m)
      return Falhas.Validacao<ContagemDto>(CodigosDaExpedicao.ContagemVazia,
          "Conte pelo menos uma unidade, conforme ou não conforme.");

    return await _execucao.ExecutarAsync(async () =>
    {
      var (peca, recusa) = await TravarAPecaAsync<ContagemDto>(pecaId, travarPedido: true, ct);
      if (recusa is not null) return recusa;
      var estado = await _leitor.CarregarAsync([peca!.No], ct);
      var nome = estado.Nome(pecaId);

      var valida = CalculoDaExpedicao.AfericaoValida(await _expedicao.ListarAfericoesAsync([pecaId], ct));
      if (CalculoDaExpedicao.Situacao(peca.No.RequerRelatorioDimensional, valida) != SituacaoDaAfericao.NaoConforme)
        return Falhas.Conflito<ContagemDto>(CodigosDaExpedicao.SemAfericaoNaoConforme,
            $"{nome} não tem aferição não conforme: só se mede unidade a unidade depois de uma.");

      var aMedir = estado.Calc.Saldo(pecaId, Local.NaExpedicao);
      if (dto.Conformes + dto.NaoConformes > aMedir)
        return Falhas.Conflito<ContagemDto>(CodigosDaExecucao.SaldoInsuficiente,
            $"Só há {Quantidades.Formatar(Math.Max(0m, aMedir))} de {nome} a medir no local de expedição.");

      var agora = DateTime.UtcNow;
      var contagem = new ContagemDimensional
      {
        EstruturaItemId = pecaId, Conformes = dto.Conformes, NaoConformes = dto.NaoConformes,
        DataHora = agora, UsuarioId = usuarioId,
      };
      // Duas gravacoes na mesma transacao: a primeira da o Id da contagem, que as linhas `Medicao` apontam
      // (`CK_Movimentacao_RegistroCoerente` exige o vinculo). Quem falhar depois desfaz as duas.
      _expedicao.Adicionar(contagem);
      await _execucao.SalvarAlteracoesAsync(ct);

      var linhas = new List<Movimentacao>();
      if (dto.Conformes > 0m) linhas.Add(Medicao(pecaId, dto.Conformes, Local.Conforme, contagem.Id, agora, usuarioId));
      if (dto.NaoConformes > 0m) linhas.Add(Medicao(pecaId, dto.NaoConformes, Local.NaoConforme, contagem.Id, agora, usuarioId));
      foreach (var linha in linhas) _execucao.Adicionar(linha);
      await _execucao.SalvarAlteracoesAsync(ct);
      // As duas posicoes ficam no local de expedicao, entao o status nao deveria mudar; recalcula-se mesmo
      // assim, porque a linha toca o fim do caminho (decisao P4 do plano de backend da Fase 5A).
      await _fechamento.RecalcularAsync(peca.Pedidos, agora, ct);

      return Result<ContagemDto>.Ok(new ContagemDto(
          contagem.Id, contagem.Conformes, contagem.NaoConformes, contagem.DataHora, usuarioId,
          await NomeDoUsuarioAsync(usuarioId, ct), Estornada: false, Estornavel: true));
    }, ct);
  }

  public async Task<Result<MovimentacaoDto>> MandarParaConserto(
      int pecaId, NovoConsertoDto dto, int usuarioId, CancellationToken ct)
  {
    if (!Quantidades.CabeNaColuna(dto.Quantidade))
      return Falhas.QuantidadeInvalida<MovimentacaoDto>(dto.Quantidade);

    return await _execucao.ExecutarAsync(async () =>
    {
      var (peca, recusa) = await TravarAPecaAsync<MovimentacaoDto>(pecaId, travarPedido: true, ct);
      if (recusa is not null) return recusa;

      // Qualquer Setor ativo, inclusive o Setor Expedicao (C19). Depois de mandado, nada mais confere o Setor
      // (decisao P10 do plano de backend da Fase 5A): inativar o Setor nao prende a peca la.
      if (await _setores.ObterPorIdAsync(dto.SetorId, ct) is not { Ativo: true })
        return Falhas.Validacao<MovimentacaoDto>(CodigosDaExpedicao.SetorInvalido,
            "O conserto vai a um Setor ativo: escolha outro.");

      var estado = await _leitor.CarregarAsync([peca!.No], ct);
      var naoConformes = estado.Calc.Saldo(pecaId, Local.NaoConforme);
      if (dto.Quantidade > naoConformes)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.SaldoInsuficiente,
            $"Só há {Quantidades.Formatar(Math.Max(0m, naoConformes))} de {estado.Nome(pecaId)} não conforme "
            + "no local de expedição.");

      var movimento = NovoMovimento.De(
          pecaId, TiposDeMovimentacao.Conserto, dto.Quantidade, Local.NaoConforme, Local.AguardandoConserto(dto.SetorId),
          usuarioId);
      _execucao.Adicionar(movimento);
      await _execucao.SalvarAlteracoesAsync(ct);
      // O conserto tira a unidade do local de expedicao: um Pedido `AguardandoExpedicao` volta a `EmProducao` (C11).
      await _fechamento.RecalcularAsync(peca.Pedidos, DateTime.UtcNow, ct);
      return Result<MovimentacaoDto>.Ok((await _projetor.ProjetarMovimentacoesAsync([movimento], ct)).Single());
    }, ct);
  }

  /// <summary>O comeco comum das tres escritas: <see cref="TravaDaPeca.TravarAsync{T}"/>.</summary>
  private Task<(PecaTravada? Peca, Result<T>? Recusa)> TravarAPecaAsync<T>(
      int pecaId, bool travarPedido, CancellationToken ct) =>
      TravaDaPeca.TravarAsync<T>(_execucao, _leitor, pecaId, travarPedido, ct);

  private static Movimentacao Medicao(int pecaId, decimal quantidade, Local destino, int contagemId, DateTime agora, int usuarioId)
  {
    var linha = NovoMovimento.De(pecaId, TiposDeMovimentacao.Medicao, quantidade, Local.NaExpedicao, destino, usuarioId);
    linha.ContagemId = contagemId;
    linha.DataHora = agora;
    return linha;
  }

  private static string Rotulo(string? resultado) =>
      resultado == ResultadosDeAfericao.NaoConforme ? "não conforme" : "conforme";

  private async Task<string> NomeDoUsuarioAsync(int usuarioId, CancellationToken ct) =>
      (await _execucao.ListarNomesDeUsuariosAsync([usuarioId], ct)).GetValueOrDefault(usuarioId, string.Empty);
}

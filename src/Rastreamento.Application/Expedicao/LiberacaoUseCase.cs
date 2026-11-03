using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Expedicao;

/// <summary>
/// A liberacao da nao conforme (D7, C5, C6 e C18 da spec da Fase 5A, secao 3.6): a Qualidade PEDE, com
/// justificativa, que a Gestao libere unidades `NaoConforme` (elas vao a `AguardandoLiberacao`); a Gestao
/// RESPONDE com a quantidade liberada, de zero ate a pedida (a liberada vai a `Liberada`, o resto volta a
/// `NaoConforme`). Pedido e resposta sao registros de `dbo.LiberacaoDimensional` (decisao P2 do plano de backend
/// da Fase 5A), gravados na transacao das linhas do livro que os apontam (`Movimentacao.LiberacaoId`). Toda
/// escrita trava a Peca e depois o Pedido (spec da Fase 3, secao 8.1): a trava da Peca serializa o pedido, a
/// resposta e o estorno do mesmo pedido, e por isso "pendente" e sempre relido depois dela.
/// </summary>
public sealed class LiberacaoUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly IExpedicaoRepository _expedicao;
  private readonly LeitorDeEstado _leitor;
  private readonly FechamentoDoPedido _fechamento;

  public LiberacaoUseCase(
      IExecucaoRepository execucao, IExpedicaoRepository expedicao, IEstruturaRepository estruturas,
      IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _expedicao = expedicao;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _fechamento = new FechamentoDoPedido(execucao);
  }

  public async Task<Result<LiberacaoDto>> Pedir(
      int pecaId, NovoPedidoDeLiberacaoDto dto, int usuarioId, CancellationToken ct)
  {
    if (!Quantidades.CabeNaColuna(dto.Quantidade))
      return Falhas.QuantidadeInvalida<LiberacaoDto>(dto.Quantidade);
    var justificativa = dto.Justificativa?.Trim();
    if (string.IsNullOrEmpty(justificativa))
      return Falhas.Validacao<LiberacaoDto>(CodigosDaExpedicao.JustificativaObrigatoria,
          "Diga por que a Gestão deveria liberar estas unidades.");
    if (TextoLongoDemais(justificativa) is { } longa) return longa;

    return await _execucao.ExecutarAsync(async () =>
    {
      var (peca, recusa) = await TravaDaPeca.TravarAsync<LiberacaoDto>(_execucao, _leitor, pecaId, travarPedido: true, ct);
      if (recusa is not null) return recusa;
      var estado = await _leitor.CarregarAsync([peca!.No], ct);
      var nome = estado.Nome(pecaId);

      // No maximo um em aberto por Peca (C18): relido depois da trava da Peca.
      var (registros, linhas) = await LerLiberacoesAsync(pecaId, ct);
      var estornadas = LinhasEstornadas(linhas);
      var porRegistro = linhas.ToLookup(l => l.LiberacaoId!.Value);
      var respostas = registros.Where(r => r.Tipo == TiposDeLiberacao.Resposta).ToList();
      if (registros.FirstOrDefault(r => EstaPendente(r, respostas, estornadas, porRegistro)) is { } emAberto)
        return Falhas.Conflito<LiberacaoDto>(CodigosDaExpedicao.LiberacaoEmAberto,
            $"{nome} já tem um pedido de liberação de {Quantidades.Formatar(emAberto.Quantidade)} em aberto. "
            + "Espere a resposta da Gestão, ou estorne esse pedido e peça de novo.");

      var naoConformes = estado.Calc.Saldo(pecaId, Local.NaoConforme);
      if (dto.Quantidade > naoConformes)
        return Falhas.Conflito<LiberacaoDto>(CodigosDaExecucao.SaldoInsuficiente,
            $"Só há {Quantidades.Formatar(Math.Max(0m, naoConformes))} de {nome} não conforme "
            + "no local de expedição.");

      var agora = DateTime.UtcNow;
      var pedido = new LiberacaoDimensional
      {
        EstruturaItemId = pecaId, Tipo = TiposDeLiberacao.Pedido, Quantidade = dto.Quantidade,
        Texto = justificativa, DataHora = agora, UsuarioId = usuarioId,
      };
      // Duas gravacoes na mesma transacao: a primeira da o Id do pedido, que a linha `PedidoDeLiberacao` aponta
      // (`CK_Movimentacao_RegistroCoerente` exige o vinculo). Quem falhar depois desfaz as duas.
      _expedicao.Adicionar(pedido);
      await _execucao.SalvarAlteracoesAsync(ct);

      _execucao.Adicionar(Linha(pecaId, TiposDeMovimentacao.PedidoDeLiberacao, dto.Quantidade,
          Local.NaoConforme, Local.AguardandoLiberacao, pedido.Id, agora, usuarioId));
      await _execucao.SalvarAlteracoesAsync(ct);
      // As duas posicoes ficam no local de expedicao; recalcula-se porque a linha toca o fim do caminho
      // (decisao P4 do plano de backend da Fase 5A).
      await _fechamento.RecalcularAsync(peca.Pedidos, agora, ct);

      return Result<LiberacaoDto>.Ok(await ParaDtoAsync(pedido, ct));
    }, ct);
  }

  public async Task<Result<LiberacaoDto>> Responder(
      int pedidoId, RespostaDeLiberacaoDto dto, int usuarioId, CancellationToken ct)
  {
    // Zero e a recusa total (C5); fora isso, a regra de quantidade de sempre.
    if (dto.QuantidadeLiberada != 0m && !Quantidades.CabeNaColuna(dto.QuantidadeLiberada))
      return Falhas.QuantidadeInvalida<LiberacaoDto>(dto.QuantidadeLiberada);
    var observacao = dto.Observacao?.Trim();
    if (string.IsNullOrEmpty(observacao)) observacao = null;
    if (observacao is not null && TextoLongoDemais(observacao) is { } longa) return longa;

    return await _execucao.ExecutarAsync(async () =>
    {
      // O registro e so de inclusao: lido antes da trava, so para saber qual Peca travar e quanto foi pedido.
      // O id de uma Resposta nao e pedido (nada no banco impede uma resposta apontando outra: quem recusa e aqui).
      var pedido = await _expedicao.ObterLiberacaoAsync(pedidoId, ct);
      if (pedido is not { Tipo: TiposDeLiberacao.Pedido }) return Falhas.NaoEncontrado<LiberacaoDto>();
      if (dto.QuantidadeLiberada > pedido.Quantidade)
        return Falhas.Validacao<LiberacaoDto>(CodigosDaExpedicao.QuantidadeAcimaDaPedida,
            $"Foram pedidas {Quantidades.Formatar(pedido.Quantidade)}: libere de zero até essa quantidade.");

      var pecaId = pedido.EstruturaItemId;
      var (peca, recusa) = await TravaDaPeca.TravarAsync<LiberacaoDto>(_execucao, _leitor, pecaId, travarPedido: true, ct);
      if (recusa is not null) return recusa;

      var (registros, linhas) = await LerLiberacoesAsync(pecaId, ct);
      var respostas = registros.Where(r => r.Tipo == TiposDeLiberacao.Resposta).ToList();
      if (!EstaPendente(pedido, respostas, LinhasEstornadas(linhas), linhas.ToLookup(l => l.LiberacaoId!.Value)))
      {
        var nome = (await _leitor.CarregarAsync([peca!.No], ct)).Nome(pecaId);
        return Falhas.Conflito<LiberacaoDto>(CodigosDaExpedicao.LiberacaoNaoPendente,
            $"O pedido de liberação de {nome} não está mais em aberto: já foi respondido ou estornado.");
      }

      var agora = DateTime.UtcNow;
      var resposta = new LiberacaoDimensional
      {
        EstruturaItemId = pecaId, Tipo = TiposDeLiberacao.Resposta, PedidoDeLiberacaoId = pedido.Id,
        Quantidade = dto.QuantidadeLiberada, Texto = observacao, DataHora = agora, UsuarioId = usuarioId,
      };
      // Como no pedido: a primeira gravacao da o Id que as linhas `Liberacao` e `Recusa` apontam.
      _expedicao.Adicionar(resposta);
      await _execucao.SalvarAlteracoesAsync(ct);

      var resto = pedido.Quantidade - dto.QuantidadeLiberada;
      if (dto.QuantidadeLiberada > 0m)
        _execucao.Adicionar(Linha(pecaId, TiposDeMovimentacao.Liberacao, dto.QuantidadeLiberada,
            Local.AguardandoLiberacao, Local.Liberada, resposta.Id, agora, usuarioId));
      if (resto > 0m)
        _execucao.Adicionar(Linha(pecaId, TiposDeMovimentacao.Recusa, resto,
            Local.AguardandoLiberacao, Local.NaoConforme, resposta.Id, agora, usuarioId));
      await _execucao.SalvarAlteracoesAsync(ct);
      await _fechamento.RecalcularAsync(peca!.Pedidos, agora, ct);

      return Result<LiberacaoDto>.Ok(await ParaDtoAsync(resposta, ct));
    }, ct);
  }

  /// <summary>
  /// O pedido esta pendente (decisao P2 do plano de backend da Fase 5A) quando a linha `PedidoDeLiberacao` dele
  /// nao foi estornada e nenhuma resposta a ele tem linha nao estornada. `respostas` pode trazer respostas de
  /// outros pedidos: so contam as que apontam este. `linhasPorLiberacao` agrupa as linhas do livro (originais e
  /// estornos) por `LiberacaoId`; `linhasEstornadas` sao os Ids das linhas que um Estorno aponta.
  /// </summary>
  internal static bool EstaPendente(
      LiberacaoDimensional pedido, IReadOnlyList<LiberacaoDimensional> respostas,
      IReadOnlySet<int> linhasEstornadas, ILookup<int, Movimentacao> linhasPorLiberacao) =>
      pedido.Tipo == TiposDeLiberacao.Pedido
      && EstaValido(pedido.Id, linhasEstornadas, linhasPorLiberacao)
      && RespostaValida(pedido, respostas, linhasEstornadas, linhasPorLiberacao) is null;

  /// <summary>A resposta ao pedido que ainda vale (tem linha nao estornada), ou nula. No maximo uma (secao 3.6).</summary>
  internal static LiberacaoDimensional? RespostaValida(
      LiberacaoDimensional pedido, IReadOnlyList<LiberacaoDimensional> respostas,
      IReadOnlySet<int> linhasEstornadas, ILookup<int, Movimentacao> linhasPorLiberacao) =>
      respostas.FirstOrDefault(r =>
          r.Tipo == TiposDeLiberacao.Resposta && r.PedidoDeLiberacaoId == pedido.Id
          && EstaValido(r.Id, linhasEstornadas, linhasPorLiberacao));

  /// <summary>
  /// O registro vale quando alguma linha original dele (nao o Estorno) nao foi estornada. O estorno de um
  /// registro estorna todas as linhas dele numa transacao, entao "alguma" e "todas" coincidem.
  /// </summary>
  internal static bool EstaValido(
      int liberacaoId, IReadOnlySet<int> linhasEstornadas, ILookup<int, Movimentacao> linhasPorLiberacao) =>
      linhasPorLiberacao[liberacaoId].Any(l => l.Tipo != TiposDeMovimentacao.Estorno && !linhasEstornadas.Contains(l.Id));

  /// <summary>Das linhas dadas, os Ids que um Estorno entre elas aponta.</summary>
  internal static IReadOnlySet<int> LinhasEstornadas(IEnumerable<Movimentacao> linhas) =>
      linhas.Where(l => l.Tipo == TiposDeMovimentacao.Estorno && l.EstornoDeId is not null)
          .Select(l => l.EstornoDeId!.Value).ToHashSet();

  private async Task<(IReadOnlyList<LiberacaoDimensional> Registros, IReadOnlyList<Movimentacao> Linhas)> LerLiberacoesAsync(
      int pecaId, CancellationToken ct)
  {
    var registros = await _expedicao.ListarLiberacoesAsync([pecaId], ct);
    var linhas = registros.Count == 0
        ? []
        : await _expedicao.ListarLinhasDasLiberacoesAsync(registros.Select(r => r.Id).ToList(), ct);
    return (registros, linhas);
  }

  private static Result<LiberacaoDto>? TextoLongoDemais(string texto) =>
      texto.Length > CodigosDaExpedicao.TamanhoMaximoDoTexto
          ? Falhas.Validacao<LiberacaoDto>(CodigosDaExpedicao.TextoLongoDemais,
              $"O texto tem no máximo {CodigosDaExpedicao.TamanhoMaximoDoTexto} caracteres.")
          : null;

  private static Movimentacao Linha(
      int pecaId, string tipo, decimal quantidade, Local de, Local para, int liberacaoId, DateTime agora, int usuarioId)
  {
    var linha = NovoMovimento.De(pecaId, tipo, quantidade, de, para, usuarioId);
    linha.LiberacaoId = liberacaoId;
    linha.DataHora = agora;
    return linha;
  }

  /// <summary>Recem-gravado: ninguem o estornou, e quem le e o autor (C7).</summary>
  private async Task<LiberacaoDto> ParaDtoAsync(LiberacaoDimensional r, CancellationToken ct) =>
      new(r.Id, r.Tipo, r.PedidoDeLiberacaoId, r.EstruturaItemId, r.Quantidade, r.Texto, r.DataHora, r.UsuarioId,
          (await _execucao.ListarNomesDeUsuariosAsync([r.UsuarioId], ct)).GetValueOrDefault(r.UsuarioId, string.Empty),
          Estornada: false, Estornavel: true);
}

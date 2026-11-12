using Rastreamento.Application.Baixa;
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Retrabalho;

/// <summary>
/// Abrir o Pedido de Retrabalho (secao 4.2 da spec da Fase 5B-2) pelas tres portas: da baixa de perda, da reclamacao
/// (consertar ou refazer) e avulsa. Uma transacao so: o Pedido `Retrabalho` com a origem e o motivo, um Agrupamento com
/// o nome e o Tipo do da Peca original, a copia da Peca (`CopiaDaPeca`, secao 4.3; so a Peca no consertar, secao 4.7),
/// o vinculo na baixa ou na reclamacao (C2) e, no consertar, a `Devolucao` (C13). O original nao muda (C9).
///
/// A concorrencia (decisao P5 do plano de backend da Fase 5B-2): trava a arvore da Peca, em ordem crescente de Id, e
/// depois o Pedido original (spec da Fase 3, secao 8.1), e so ENTAO rele o alvo. Duas aberturas da mesma baixa ou da
/// mesma reclamacao travam o mesmo Pedido: a segunda espera a primeira e ve o vinculo dela (409 `JaReposto`).
/// </summary>
public sealed partial class RetrabalhoUseCase
{
  private const string StatusAberto = "Aberto";
  private const string TipoRetrabalho = "Retrabalho";

  private readonly IExecucaoRepository _execucao;
  private readonly IReposicaoRepository _reposicao;
  private readonly IBaixaRepository _baixas;
  private readonly IEstruturaRepository _estruturas;
  private readonly IAgrupamentoRepository _agrupamentos;
  private readonly IPedidoRepository _pedidos;
  private readonly ISetorRepository _setores;
  private readonly CadastroDePedidoUseCase _cadastro;
  private readonly LeitorDeEstado _leitor;
  private readonly FechamentoDoPedido _fechamento;

  public RetrabalhoUseCase(
      IExecucaoRepository execucao, IReposicaoRepository reposicao, IBaixaRepository baixas,
      IEstruturaRepository estruturas, IAgrupamentoRepository agrupamentos, IPedidoRepository pedidos,
      ISetorRepository setores, IReceitaPadraoRepository catalogo, CadastroDePedidoUseCase cadastro)
  {
    _execucao = execucao;
    _reposicao = reposicao;
    _baixas = baixas;
    _estruturas = estruturas;
    _agrupamentos = agrupamentos;
    _pedidos = pedidos;
    _setores = setores;
    _cadastro = cadastro;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _fechamento = new FechamentoDoPedido(execucao);
  }

  /// <summary>
  /// Abre o Retrabalho do Pedido <paramref name="pedidoOrigemId"/>. O corpo e conferido antes da transacao
  /// (<see cref="ConferirCorpo"/>), e o alvo e lido antes dela (<see cref="LerAlvoAsync"/>: existir no original, 404).
  /// Dentro dela, nesta ordem: as travas, a releitura do alvo (<see cref="ConferirAlvoAsync"/>), o Setor do conserto
  /// (400 `SetorInvalido`), a copia (`CopiaDaPeca`) e o numero unico (o 409 do cadastro de Pedido). Devolve o Retrabalho
  /// como o cadastro de Pedido o projeta.
  /// </summary>
  public async Task<Result<PedidoDto>> Abrir(int pedidoOrigemId, NovoRetrabalhoDto dto, int usuarioId, CancellationToken ct)
  {
    var (corpo, recusaDoCorpo) = ConferirCorpo(dto);
    if (recusaDoCorpo is not null) return recusaDoCorpo;

    // O alvo e a Peca dele, lidos ANTES da transacao so para saber o que travar: a Peca de uma baixa (as linhas dela) e
    // a de uma reclamacao nao mudam depois de gravadas. O que muda (o vinculo, o estorno) e relido depois das travas.
    // A arvore a travar tambem e lida fora: lida dentro, sob SERIALIZABLE, a trava de faixa dela ficaria com a segunda
    // abertura enquanto ela espera os nos, e a primeira, ao gravar a arvore copiada, cairia nessa faixa (deadlock).
    var lido = await _execucao.ConsultarAsync(async () =>
    {
      var lida = await LerAlvoAsync(pedidoOrigemId, corpo!, ct);
      return lida.Sucesso
          ? Result<Alvo>.Ok(lida.Valor! with { Subarvore = await _execucao.ListarIdsDaSubarvoreAsync(lida.Valor!.PecaId, ct) })
          : lida;
    }, ct);
    if (!lido.Sucesso) return Falhas.Repassar<PedidoDto, Alvo>(lido);
    var alvo = lido.Valor!;

    return await _execucao.ExecutarAsync(async () =>
    {
      if (alvo.Subarvore.Count == 0) return Falhas.NaoEncontrado<PedidoDto>();
      var nos = (await _execucao.TravarNosAsync(alvo.Subarvore, ct)).ToList();
      if (await _execucao.TravarPedidoAsync(pedidoOrigemId, ct) is null) return Falhas.NaoEncontrado<PedidoDto>();
      // Relida depois das travas: o filho acrescentado entre a leitura e a trava entra na copia, travado tambem.
      var subarvore = await _execucao.ListarIdsDaSubarvoreAsync(alvo.PecaId, ct);
      var acrescentados = subarvore.Except(nos.Select(n => n.Id)).ToList();
      if (acrescentados.Count > 0) nos.AddRange(await _execucao.TravarNosAsync(acrescentados, ct));
      nos.RemoveAll(n => !subarvore.Contains(n.Id));

      var recusaDepoisDaTrava = await ConferirAlvoAsync(pedidoOrigemId, corpo!, alvo, nos, ct);
      if (recusaDepoisDaTrava is not null) return recusaDepoisDaTrava;

      if (corpo!.Destino == DestinosDaReclamacao.Consertar
          && await _setores.ObterPorIdAsync(corpo.SetorId!.Value, ct) is not { Ativo: true })
        return Falhas.Validacao<PedidoDto>(CodigosDaExpedicao.SetorInvalido,
            "O conserto vai a um Setor ativo: escolha outro.");

      var (plano, recusaDaCopia) = await PlanejarCopiaAsync(nos, alvo, corpo.Destino == DestinosDaReclamacao.Consertar, ct);
      if (recusaDaCopia is not null) return recusaDaCopia;

      // A guarda do numero unico e a do cadastro de Pedido (C7), lida dentro da transacao.
      if (await _cadastro.RecusarNumeroEmUsoAsync(corpo.Numero, null, ct) is { } numeroEmUso) return numeroEmUso;

      return await GravarAsync(pedidoOrigemId, corpo, alvo, nos, plano!, usuarioId, ct);
    }, ct);
  }

  /// <summary>O corpo ja conferido: a porta, o numero aparado e a data de entrega (nula: a do original).</summary>
  private sealed record CorpoConferido(
      Porta Porta, int? PerdaId, int? ReclamacaoId, int? EstruturaItemId, decimal? Quantidade, string? Destino,
      int? SetorId, string Numero, DateOnly? DataEntrega, string Motivo);

  private enum Porta { Baixa, Reclamacao, Avulsa }

  /// <summary>
  /// O alvo relido: a Peca a copiar, quantas unidades dela, e a baixa ou a reclamacao (nulas na avulsa). A quantidade
  /// da baixa sao as unidades de Peca que ela levou a `Perdido` (C1); a da reclamacao, as que voltaram.
  /// </summary>
  private sealed record Alvo(int PecaId, decimal Quantidade, Perda? Perda, IReadOnlyList<int> LinhasDaPerda, Reclamacao? Reclamacao)
  {
    /// <summary>A Peca e os descendentes dela, lidos antes da transacao: o que se trava.</summary>
    public IReadOnlyList<int> Subarvore { get; init; } = [];
  }

  /// <summary>
  /// As recusas do corpo, sem banco. A porta: no maximo uma de `PerdaId` e `ReclamacaoId`; `EstruturaItemId` e
  /// `Quantidade` so na avulsa, que exige os dois; `Destino` e `SetorId` so na reclamacao (400 `PortaInvalida`). Na
  /// reclamacao, o destino e `Consertar` com Setor ou `Refazer` sem (400 `DestinoInvalido`). Depois, a quantidade da
  /// avulsa (400 `QuantidadeInvalida`), o motivo, um dos quatro de `CK_Pedido_MotivoRetrabalho` (400 `MotivoInvalido`;
  /// a tela sugere, C5), e o numero (400 `NumeroObrigatorio`).
  /// </summary>
  private static (CorpoConferido? Corpo, Result<PedidoDto>? Recusa) ConferirCorpo(NovoRetrabalhoDto dto)
  {
    static (CorpoConferido?, Result<PedidoDto>?) RecusaDePorta(string mensagem) =>
        (null, Falhas.Validacao<PedidoDto>(CodigosDoRetrabalho.PortaInvalida, mensagem));

    var dePeca = dto.EstruturaItemId is not null || dto.Quantidade is not null;
    var deDestino = dto.Destino is not null || dto.SetorId is not null;
    Porta porta;
    if (dto.PerdaId is not null && dto.ReclamacaoId is not null)
      return RecusaDePorta("Escolha uma porta só: a baixa ou a reclamação.");
    if (dto.PerdaId is not null)
    {
      if (dePeca || deDestino)
        return RecusaDePorta("O Retrabalho da baixa copia a Peça que ela levou, nas unidades que levou: não se informa Peça, quantidade nem destino.");
      porta = Porta.Baixa;
    }
    else if (dto.ReclamacaoId is not null)
    {
      if (dePeca)
        return RecusaDePorta("O Retrabalho da reclamação copia a Peça reclamada, nas unidades que voltaram: não se informa Peça nem quantidade.");
      var destinoValido = dto.Destino switch
      {
        DestinosDaReclamacao.Consertar => dto.SetorId is not null,
        DestinosDaReclamacao.Refazer => dto.SetorId is null,
        _ => false,
      };
      if (!destinoValido)
        return (null, Falhas.Validacao<PedidoDto>(CodigosDoRetrabalho.DestinoInvalido,
            "Diga o destino da Peça reclamada: consertar, com o Setor do conserto, ou refazer inteira."));
      porta = Porta.Reclamacao;
    }
    else
    {
      if (dto.EstruturaItemId is null) return RecusaDePorta("Diga de onde vem o Retrabalho: uma baixa, uma reclamação ou uma Peça do Pedido.");
      if (deDestino) return RecusaDePorta("O destino e o Setor são só do Retrabalho de uma reclamação.");
      if (dto.Quantidade is not decimal quantidade || !Quantidades.CabeNaColuna(quantidade))
        return (null, Falhas.QuantidadeInvalida<PedidoDto>(dto.Quantidade ?? 0m));
      porta = Porta.Avulsa;
    }

    if (dto.MotivoRetrabalho is null || !MotivosDeRetrabalho.Todos.Contains(dto.MotivoRetrabalho))
      return (null, Falhas.Validacao<PedidoDto>(CodigosDaBaixa.MotivoInvalido,
          $"O motivo do Retrabalho é um destes: {string.Join(", ", MotivosDeRetrabalho.Todos)}."));

    var numero = dto.Numero?.Trim() ?? string.Empty;
    if (numero.Length == 0)
      return (null, Falhas.Validacao<PedidoDto>(CodigosDoRetrabalho.NumeroObrigatorio, "Diga o número do Retrabalho."));

    return (new CorpoConferido(porta, dto.PerdaId, dto.ReclamacaoId, dto.EstruturaItemId, dto.Quantidade, dto.Destino,
        dto.SetorId, numero, dto.DataEntrega, dto.MotivoRetrabalho), null);
  }
}

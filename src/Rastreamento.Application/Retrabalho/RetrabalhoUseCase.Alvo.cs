using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Retrabalho;

/// <summary>
/// O alvo da abertura (a baixa, a reclamacao ou a Peca da avulsa), a copia e a gravacao do Retrabalho (secoes 4.2, 4.3
/// e 4.7 da spec da Fase 5B-2).
/// </summary>
public sealed partial class RetrabalhoUseCase
{
  /// <summary>
  /// O alvo e a Peca dele, FORA da transacao (antes das travas). A baixa e a reclamacao tem de ser do Pedido original (404). A baixa so
  /// se repoe se for de perda (`PerdaArmazem` ou `MortaEmProcesso`) e se levou unidades da Peca do topo, a propria ou
  /// pelo `incluirTopo` (409 `SemPecaARepor`, C1): o motivo e as linhas sao so de inclusao, e por isso se leem aqui.
  /// Fora da transacao de proposito: lido nela, sob SERIALIZABLE, a trava compartilhada da linha da baixa ficaria com
  /// a segunda abertura enquanto ela espera a arvore, e a primeira, ao gravar o vinculo, entraria em deadlock com ela.
  /// </summary>
  private async Task<Result<Alvo>> LerAlvoAsync(
      int pedidoOrigemId, CorpoConferido corpo, CancellationToken ct)
  {
    switch (corpo.Porta)
    {
      case Porta.Baixa:
      {
        var perda = await _baixas.ObterAsync(corpo.PerdaId!.Value, ct);
        if (perda is null || perda.PedidoId != pedidoOrigemId) return Falhas.NaoEncontrado<Alvo>();
        if (!MotivosDePerda.QueSeRepoem.Contains(perda.Motivo))
          return SemPecaARepor<Alvo>("Só a baixa de perda se repõe: o descarte é o que saiu do projeto.");

        var linhas = (await _baixas.ListarLinhasDasPerdasAsync([perda.Id], ct))
            .Where(l => l.Tipo == TiposDeMovimentacao.Perda).ToList();
        var pecas = (await _execucao.ListarNosAsync(linhas.Select(l => l.EstruturaItemId).Distinct().ToList(), ct))
            .Where(n => n.EstruturaPaiId is null).Select(n => n.Id).ToHashSet();
        // Uma baixa e de um no so, e a Peca dela e a do topo desse no: no maximo uma.
        var daPeca = linhas.Where(l => pecas.Contains(l.EstruturaItemId)).ToList();
        if (daPeca.Count == 0)
          return SemPecaARepor<Alvo>("Esta baixa não levou nenhuma unidade da Peça: não há Peça a repor.");
        var pecaId = daPeca.Min(l => l.EstruturaItemId);
        return Result<Alvo>.Ok(new Alvo(pecaId, daPeca.Where(l => l.EstruturaItemId == pecaId).Sum(l => l.Quantidade), perda,
            linhas.Select(l => l.Id).ToList(), null));
      }
      case Porta.Reclamacao:
      {
        var reclamacao = await _reposicao.ObterReclamacaoAsync(corpo.ReclamacaoId!.Value, ct);
        if (reclamacao is null || reclamacao.PedidoId != pedidoOrigemId) return Falhas.NaoEncontrado<Alvo>();
        return Result<Alvo>.Ok(new Alvo(reclamacao.EstruturaItemId, reclamacao.Quantidade, null, [], reclamacao));
      }
      default:
        return Result<Alvo>.Ok(new Alvo(corpo.EstruturaItemId!.Value, corpo.Quantidade!.Value, null, [], null));
    }
  }

  /// <summary>
  /// O alvo relido DEPOIS das travas (decisao P5 do plano de backend da Fase 5B-2): e aqui que entram as recusas que
  /// dependem do que outra escrita pode ter mudado. A baixa e a reclamacao: ja ter Retrabalho (409 `JaReposto`, D9), ter
  /// "nao repor" vigente (409 `ReposicaoDispensada`) e ter sido estornada (409 `SemPecaARepor`). A avulsa: o no ser do Pedido original (404) e ser Peca (400 `NaoEhPeca`).
  /// </summary>
  private async Task<Result<PedidoDto>?> ConferirAlvoAsync(
      int pedidoOrigemId, CorpoConferido corpo, Alvo alvo, IReadOnlyList<EstruturaItem> nos, CancellationToken ct)
  {
    switch (corpo.Porta)
    {
      case Porta.Baixa:
      {
        var perda = (await _baixas.ObterAsync(alvo.Perda!.Id, ct))!;
        if (perda.PedidoRetrabalhoId is int reposta) return await JaRepostoAsync("Esta baixa", reposta, ct);
        if ((await _reposicao.ListarDispensasVigentesAsync([perda.Id], [], ct)).Count > 0)
          return ReposicaoDispensada("Esta baixa");
        if ((await _execucao.ListarEstornadasAsync(alvo.LinhasDaPerda, ct)).Count > 0)
          return SemPecaARepor<PedidoDto>("Esta baixa foi estornada: não há Peça a repor.");
        return null;
      }
      case Porta.Reclamacao:
      {
        var reclamacao = (await _reposicao.ObterReclamacaoAsync(alvo.Reclamacao!.Id, ct))!;
        if (reclamacao.PedidoRetrabalhoId is int reposta) return await JaRepostoAsync("Esta reclamação", reposta, ct);
        if ((await _reposicao.ListarDispensasVigentesAsync([], [reclamacao.Id], ct)).Count > 0)
          return ReposicaoDispensada("Esta reclamação");
        if (reclamacao.EstornadaEm is not null)
          return SemPecaARepor<PedidoDto>("Esta reclamação foi estornada: não há Peça a repor.");
        return null;
      }
      default:
      {
        var no = nos.SingleOrDefault(n => n.Id == alvo.PecaId);
        if (no is null || (await _execucao.ObterPedidoDoNoAsync(no.Id, ct))?.PedidoId != pedidoOrigemId)
          return Falhas.NaoEncontrado<PedidoDto>();
        if (no.EstruturaPaiId is not null)
        {
          var estado = await _leitor.CarregarAsync([no], ct);
          return Falhas.Validacao<PedidoDto>(CodigosDaExpedicao.NaoEhPeca,
              $"{estado.Nome(no.Id)} é um Item: o Retrabalho copia uma Peça do Pedido.");
        }
        return null;
      }
    }
  }

  private async Task<Result<PedidoDto>> JaRepostoAsync(string alvo, int retrabalhoId, CancellationToken ct)
  {
    var numero = (await _pedidos.ObterPorIdAsync(retrabalhoId, ct))?.Numero;
    return Falhas.Conflito<PedidoDto>(CodigosDoRetrabalho.JaReposto,
        $"{alvo} já tem o Retrabalho {numero}: um Retrabalho por baixa ou reclamação.");
  }

  /// <summary>
  /// O alvo com "nao repor" vigente nao abre Retrabalho (D3 e secao 3.4 da spec da Fase 5B-2): e preciso desfazer a
  /// dispensa antes. Lido depois da trava do Pedido, a mesma que o "nao repor" toma.
  /// </summary>
  private static Result<PedidoDto> ReposicaoDispensada(string alvo) =>
      Falhas.Conflito<PedidoDto>(CodigosDoRetrabalho.ReposicaoDispensada,
          $"{alvo} está dispensada de repor: desfaça o “não repor” para abrir o Retrabalho.");

  private static Result<T> SemPecaARepor<T>(string mensagem) =>
      Falhas.Conflito<T>(CodigosDoRetrabalho.SemPecaARepor, mensagem);

  /// <summary>
  /// A arvore da Peca, ja travada, como `CopiaDaPeca` a le, e o plano dela. Os nos que sairam do projeto ficam de fora
  /// da copia (C22 da spec da Fase 5B-1). A copia escala pela razao com o pai e, no material, pela quantidade do proprio
  /// no: um no de quantidade zero nao tem razao, e a estrutura com um deles e recusada aqui, com frase propria.
  /// </summary>
  private async Task<(NoParaGravar? Plano, Result<PedidoDto>? Recusa)> PlanejarCopiaAsync(
      IReadOnlyList<EstruturaItem> nos, Alvo alvo, bool soAPeca, CancellationToken ct)
  {
    if (nos.FirstOrDefault(n => n.Quantidade <= 0m) is { } semQuantidade)
    {
      var estado = await _leitor.CarregarAsync([semQuantidade], ct);
      return (null, Falhas.Validacao<PedidoDto>(CodigosDaExecucao.QuantidadeInvalida,
          $"{estado.Nome(semQuantidade.Id)} está com quantidade zero: corrija a estrutura antes de abrir o Retrabalho."));
    }

    var ids = nos.Select(n => n.Id).ToList();
    var materiais = (await _estruturas.ListarMateriaisAsync(ids, ct)).ToLookup(m => m.EstruturaItemId);
    var roteiro = (await _estruturas.ListarRoteiroAsync(ids, ct)).ToLookup(r => r.EstruturaItemId);
    var saidos = await _execucao.ListarSaidosDoProjetoAsync(ids, ct);
    var arvore = nos.Select(n => new NoDaCopia(
        n.Id, n.EstruturaPaiId, n.ComponenteId, n.Descricao, n.Quantidade, n.QuantidadePorPai, n.RequerRelatorioDimensional,
        materiais[n.Id].Select(m => (m.MaterialId, m.Quantidade)).ToList(),
        roteiro[n.Id].Select(r => (r.SetorId, r.Ordem)).ToList())).ToList();

    var plano = CopiaDaPeca.Planejar(arvore, alvo.PecaId, alvo.Quantidade, saidos, soAPeca);
    return plano.Sucesso ? (plano.Valor, null) : (null, Falhas.Repassar<PedidoDto, NoParaGravar>(plano));
  }

  /// <summary>
  /// Grava o Retrabalho: o Pedido (o cliente e, por padrao, a data de entrega vem do original, C7), um Agrupamento com o
  /// nome e o Tipo do da Peca, a arvore copiada, o vinculo e, no consertar, a `Devolucao` da quantidade inteira para
  /// `AguardandoConserto` no Setor (C13), que poe o Retrabalho em producao (regra 28). Recalcula o fechamento dele, como
  /// toda escrita que muda a producao de um Pedido; o do original nao muda (C9).
  /// </summary>
  private async Task<Result<PedidoDto>> GravarAsync(
      int pedidoOrigemId, CorpoConferido corpo, Alvo alvo, IReadOnlyList<EstruturaItem> nos, NoParaGravar plano,
      int usuarioId, CancellationToken ct)
  {
    var original = (await _pedidos.ObterPorIdAsync(pedidoOrigemId, ct))!;
    var agrupamentoDaPeca = (await _agrupamentos.ObterPorIdAsync(nos.Single(n => n.Id == alvo.PecaId).AgrupamentoId, ct))!;
    var agora = DateTime.UtcNow;

    var retrabalho = new Pedido
    {
      Numero = corpo.Numero, Cliente = original.Cliente, Tipo = TipoRetrabalho, PedidoOrigemId = pedidoOrigemId,
      MotivoRetrabalho = corpo.Motivo, Status = StatusAberto, DataEntrega = corpo.DataEntrega ?? original.DataEntrega,
      DataAbertura = agora, CriadoPorUsuarioId = usuarioId,
    };
    await _pedidos.AdicionarAsync(retrabalho, ct);
    await _execucao.SalvarAlteracoesAsync(ct);

    var agrupamento = new Agrupamento
    {
      PedidoId = retrabalho.Id, Codigo = agrupamentoDaPeca.Codigo, Tipo = agrupamentoDaPeca.Tipo,
      CriadoPorUsuarioId = usuarioId, CriadoEm = agora,
    };
    await _agrupamentos.AdicionarAsync(agrupamento, ct);
    await _execucao.SalvarAlteracoesAsync(ct);
    var pecaId = await _estruturas.GravarArvoreAsync(agrupamento.Id, null, plano, ct);

    // O alvo foi relido depois das travas, entao o vinculo condicional sempre grava; se nao gravasse, a transacao volta.
    var vinculou = corpo.Porta switch
    {
      Porta.Baixa => await _reposicao.MarcarPerdaRepostaAsync(alvo.Perda!.Id, retrabalho.Id, ct),
      Porta.Reclamacao => await _reposicao.MarcarReclamacaoRepostaAsync(alvo.Reclamacao!.Id, retrabalho.Id, ct),
      _ => true,
    };
    if (!vinculou)
      return Falhas.Conflito<PedidoDto>(CodigosDoRetrabalho.JaReposto, "Este alvo já tem um Retrabalho.");

    if (corpo.Destino == DestinosDaReclamacao.Consertar)
    {
      var devolucao = NovoMovimento.De(
          pecaId, TiposDeMovimentacao.Devolucao, alvo.Quantidade, Local.AIniciar, Local.AguardandoConserto(corpo.SetorId!.Value),
          usuarioId);
      devolucao.ReclamacaoId = alvo.Reclamacao!.Id;
      devolucao.DataHora = agora;
      _execucao.Adicionar(devolucao);
      await _execucao.SalvarAlteracoesAsync(ct);
      await _execucao.MarcarPedidoEmProducaoAsync(retrabalho.Id, ct);
    }
    await _fechamento.RecalcularAsync([retrabalho.Id], agora, ct);

    // As duas escritas de status sao conjuntistas e nao passam pela entidade: o status que volta e o relido do banco.
    retrabalho.Status = (await _execucao.TravarPedidoAsync(retrabalho.Id, ct))!.Status;
    return Result<PedidoDto>.Ok(_cadastro.ProjetarNovo(retrabalho, original.Numero));
  }
}

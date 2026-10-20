using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Separacao;

/// <summary>
/// As leituras da separacao de material (spec da Fase 4, secao 5.2): o historico do no e, com a arvore, o
/// catalogo e o relogio, a lista do Separador.
/// </summary>
public sealed class ConsultaDeSeparacaoUseCase
{
  private readonly ISeparacaoRepository _separacao;
  private readonly IExecucaoRepository _execucao;
  private readonly IEstruturaRepository _estruturas;
  private readonly IReceitaPadraoRepository _catalogo;
  private readonly TimeProvider _relogio;
  private readonly LeitorDeEstado _leitor;

  public ConsultaDeSeparacaoUseCase(
      ISeparacaoRepository separacao, IExecucaoRepository execucao, IEstruturaRepository estruturas,
      IReceitaPadraoRepository catalogo, TimeProvider relogio)
  {
    _separacao = separacao;
    _execucao = execucao;
    _estruturas = estruturas;
    _catalogo = catalogo;
    _relogio = relogio;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
  }

  /// <summary>
  /// O livro do no, mais recente primeiro (`DataHora`, e o `Id` desempatando). `Estornavel` e calculado
  /// para `quem`, como os `estornaveis` da Fila: uma `Separacao` ainda sem estorno, do proprio leitor, ou de
  /// qualquer autor quando ele ve os registros de todos (PCP e Administrador).
  /// </summary>
  public async Task<Result<IReadOnlyList<SeparacaoDto>>> Historico(int noId, QuemLe quem, CancellationToken ct)
  {
    if (!await _separacao.NoExisteAsync(noId, ct)) return Falhas.NaoEncontrado<IReadOnlyList<SeparacaoDto>>();

    var linhas = await _separacao.ListarLinhasAsync([noId], ct);
    var estornadas = linhas.Where(l => l.EstornoDeId is not null).Select(l => l.EstornoDeId!.Value).ToHashSet();
    var materiais = (await _separacao.ListarMateriaisAsync(linhas.Select(l => l.MaterialId).Distinct().ToList(), ct))
        .ToDictionary(m => m.Id);
    var autores = await _execucao.ListarNomesDeUsuariosAsync(linhas.Select(l => l.UsuarioId).Distinct().ToList(), ct);

    bool Estornavel(MaterialSeparacao l) =>
        l.Tipo == TiposDeSeparacao.Separacao && !estornadas.Contains(l.Id)
        && (quem.VeRegistrosDeTodos || l.UsuarioId == quem.UsuarioId);

    IReadOnlyList<SeparacaoDto> historico = linhas
        .OrderByDescending(l => l.DataHora).ThenByDescending(l => l.Id)
        .Select(l => ProjecaoDaSeparacao.Linha(
            l, materiais[l.MaterialId], autores.GetValueOrDefault(l.UsuarioId, string.Empty), Estornavel(l)))
        .ToList();
    return Result<IReadOnlyList<SeparacaoDto>>.Ok(historico);
  }

  /// <summary>
  /// A lista do Separador (spec da Fase 4, secao 5.2): todo no de Pedido aberto (nem Concluido nem Cancelado)
  /// com ao menos um material do almoxarifado pendente, do prazo de entrega mais proximo para o mais distante
  /// (`Id` do Pedido e `Id` do no desempatando). Pedido pausado continua na lista, com a pausa: separar e antecipar, e a
  /// pausa recusa so o inicio (regra 31). Pedido concluido ou cancelado nao vem de `ListarNosEmProducaoAsync`.
  /// </summary>
  public Task<Result<IReadOnlyList<PendenciaDeSeparacaoDto>>> Pendencias(CancellationToken ct) =>
      _execucao.ConsultarAsync(() => PendenciasAsync(ct), ct);

  private async Task<Result<IReadOnlyList<PendenciaDeSeparacaoDto>>> PendenciasAsync(CancellationToken ct)
  {
    var contexto = await _execucao.ListarNosEmProducaoAsync(ct);
    var comAlmoxarifado = await _separacao.ListarNosComMaterialDoAlmoxarifadoAsync(
        contexto.Select(x => x.No.Id).ToList(), ct);
    var candidatos = contexto.Where(x => comAlmoxarifado.Contains(x.No.Id)).ToList();
    if (candidatos.Count == 0)
      return Result<IReadOnlyList<PendenciaDeSeparacaoDto>>.Ok([]);

    var ids = candidatos.Select(x => x.No.Id).ToList();
    var estado = await _leitor.CarregarAsync(candidatos.Select(x => x.No).ToList(), ct);
    var planejado = await _separacao.ListarPlanejadoAsync(ids, ct);
    var linhas = await _separacao.ListarLinhasAsync(ids, ct);
    var materiaisIds = planejado.Select(p => p.MaterialId).Concat(linhas.Select(l => l.MaterialId)).Distinct().ToList();
    var materiais = (await _separacao.ListarMateriaisAsync(materiaisIds, ct)).ToDictionary(m => m.Id);
    var doAlmoxarifado = materiais.Values.Where(m => m.SetorDeEstoqueId is null).Select(m => m.Id).ToHashSet();

    var pendentesPorNo = CalculoDaSeparacao.Resumir(planejado, linhas, doAlmoxarifado)
        .Where(r => r.Pendencia > 0m)
        .ToLookup(r => r.EstruturaItemId);

    var primeirosPassos = candidatos
        .Where(x => pendentesPorNo.Contains(x.No.Id))
        .Select(x => estado.Calc.PrimeiroPasso(x.No.Id))
        .OfType<PassoDoCalculo>().Select(p => p.SetorId).Distinct().ToList();
    var setores = (await _catalogo.ObterSetoresPorIdAsync(primeirosPassos, ct)).ToDictionary(s => s.Id, s => s.Nome);

    var hoje = PrazoDeEntrega.HojeEmBrasilia(_relogio);
    IReadOnlyList<PendenciaDeSeparacaoDto> lista = candidatos
        .Where(x => pendentesPorNo.Contains(x.No.Id))
        .OrderBy(x => x.PedidoDataEntrega).ThenBy(x => x.PedidoId).ThenBy(x => x.No.Id)
        .Select(x => new PendenciaDeSeparacaoDto(
            new NoDaPendenciaDto(
                x.No.Id, estado.Nome(x.No.Id), estado.Codigos.GetValueOrDefault(x.No.Id), x.AgrupamentoId, x.AgrupamentoCodigo),
            new PedidoDaPendenciaDto(
                x.PedidoId, x.PedidoNumero, x.PedidoCliente, x.PedidoDataEntrega,
                PrazoDeEntrega.EstaAtrasado(x.PedidoDataEntrega, x.PedidoStatus, hoje), PausaResumoDto.De(x.Pausa)),
            estado.Calc.PrimeiroPasso(x.No.Id) is PassoDoCalculo passo
                ? new SetorDeDestinoDto(passo.SetorId, setores.GetValueOrDefault(passo.SetorId, string.Empty))
                : null,
            pendentesPorNo[x.No.Id]
                .Select(r => (Resumo: r, Material: materiais[r.MaterialId]))
                .OrderBy(t => t.Material.Codigo, StringComparer.Ordinal)
                .Select(t => new MaterialPendenteDto(
                    t.Material.Id, t.Material.Codigo, t.Material.Descricao, t.Material.UnidadeMedida,
                    t.Resumo.Planejado, t.Resumo.Separado, t.Resumo.Pendencia))
                .ToList()))
        .ToList();
    return Result<IReadOnlyList<PendenciaDeSeparacaoDto>>.Ok(lista);
  }
}

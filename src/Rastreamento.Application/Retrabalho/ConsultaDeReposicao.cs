using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Retrabalho;

/// <summary>Uma baixa, com o que a conta do "a repor" precisa dela (secao 4.1 da spec da Fase 5B-2).</summary>
public sealed record BaixaParaContar(
    int PedidoId, string Motivo, decimal UnidadesDePeca, bool Estornada, bool Reposta, bool Dispensada);

/// <summary>Uma reclamacao, com o que a conta do "a repor" precisa dela.</summary>
public sealed record ReclamacaoParaContar(int PedidoId, decimal Quantidade, bool Estornada, bool Reposta, bool Dispensada);

/// <summary>O que uma baixa ou uma reclamacao diz da reposicao: o Retrabalho dela, o "nao repor" vigente e o a repor.</summary>
public sealed record ReposicaoDaLinha(PedidoRetrabalhoResumoDto? PedidoRetrabalho, DispensaDto? Dispensa, decimal ARepor);

/// <summary>
/// O "a repor" (C1, C3 e C16 da spec da Fase 5B-2, secoes 4.1 e 4.6): unidades de Peca que saem por uma baixa de
/// `PerdaArmazem` ou `MortaEmProcesso` nao estornada, ou que voltam numa reclamacao nao estornada, e que ainda nao tem
/// Retrabalho nem "nao repor" vigente. Calculado na leitura, no servidor, em lote: a lista de Pedidos nao faz uma
/// consulta por Pedido. A regra de contar e funcao pura (<see cref="UnidadesARepor(BaixaParaContar)"/>,
/// <see cref="ContarPorPedido"/>), sem banco, e a parte assincrona so le e a chama.
/// </summary>
public sealed class ConsultaDeReposicao
{
  private static readonly IReadOnlySet<string> MotivosQueSeRepoem =
      new HashSet<string> { MotivosDePerda.PerdaArmazem, MotivosDePerda.MortaEmProcesso };

  private readonly IReposicaoRepository _reposicao;
  private readonly IBaixaRepository _baixas;
  private readonly IExecucaoRepository _execucao;

  public ConsultaDeReposicao(IReposicaoRepository reposicao, IBaixaRepository baixas, IExecucaoRepository execucao)
  {
    _reposicao = reposicao;
    _baixas = baixas;
    _execucao = execucao;
  }

  /// <summary>
  /// As unidades de Peca de uma baixa que ainda faltam repor: as que ela levou, so quando o motivo se repoe e a baixa
  /// nao foi estornada, nao tem Retrabalho e nao tem dispensa vigente. `Descarte` e `Reaproveitado` nunca contam.
  /// </summary>
  public static decimal UnidadesARepor(BaixaParaContar b) =>
      MotivosQueSeRepoem.Contains(b.Motivo) && !b.Estornada && !b.Reposta && !b.Dispensada ? b.UnidadesDePeca : 0m;

  /// <summary>As unidades de uma reclamacao que ainda faltam repor: a quantidade dela, enquanto vigente e sem resolver.</summary>
  public static decimal UnidadesARepor(ReclamacaoParaContar r) =>
      !r.Estornada && !r.Reposta && !r.Dispensada ? r.Quantidade : 0m;

  /// <summary>
  /// Se o "nao repor" da baixa esta vigente. Chamada DEPOIS das travas, pelo estorno da baixa (C20 da spec da Fase 5B-2).
  /// </summary>
  public async Task<bool> TemDispensaVigenteDaBaixaAsync(int perdaId, CancellationToken ct) =>
      (await _reposicao.ListarDispensasVigentesAsync([perdaId], [], ct)).Count > 0;

  /// <summary>A soma por Pedido; so os Pedidos que aparecem nas baixas e reclamacoes recebidas entram.</summary>
  public static IReadOnlyDictionary<int, decimal> ContarPorPedido(
      IEnumerable<BaixaParaContar> baixas, IEnumerable<ReclamacaoParaContar> reclamacoes)
  {
    var soma = new Dictionary<int, decimal>();
    foreach (var b in baixas) soma[b.PedidoId] = soma.GetValueOrDefault(b.PedidoId) + UnidadesARepor(b);
    foreach (var r in reclamacoes) soma[r.PedidoId] = soma.GetValueOrDefault(r.PedidoId) + UnidadesARepor(r);
    return soma;
  }

  /// <summary>
  /// O "a repor" de cada Pedido pedido, em lote: todo Pedido pedido volta, com zero quando nao ha nada a repor. Seis
  /// consultas no maximo, quantos forem os Pedidos.
  /// </summary>
  public async Task<IReadOnlyDictionary<int, decimal>> ARepor(IReadOnlyCollection<int> pedidoIds, CancellationToken ct)
  {
    var resultado = pedidoIds.Distinct().ToDictionary(id => id, _ => 0m);
    if (resultado.Count == 0) return resultado;

    var perdas = await _reposicao.ListarPerdasSemRetrabalhoAsync(resultado.Keys.ToList(), ct);
    var reclamacoes = await _reposicao.ListarReclamacoesSemRetrabalhoAsync(resultado.Keys.ToList(), ct);
    var dispensas = await _reposicao.ListarDispensasVigentesAsync(
        perdas.Select(p => p.Id).ToList(), reclamacoes.Select(r => r.Id).ToList(), ct);
    var perdasDispensadas = dispensas.Where(d => d.PerdaId is not null).Select(d => d.PerdaId!.Value).ToHashSet();
    var reclamacoesDispensadas = dispensas.Where(d => d.ReclamacaoId is not null).Select(d => d.ReclamacaoId!.Value).ToHashSet();

    IReadOnlyList<Movimentacao> linhas =
        perdas.Count == 0 ? [] : await _baixas.ListarLinhasDasPerdasAsync(perdas.Select(p => p.Id).ToList(), ct);
    var nos = await NosDasLinhasAsync(linhas, ct);

    var baixasParaContar = perdas.Select(p => ParaContar(p, linhas, nos, perdasDispensadas.Contains(p.Id)));
    var reclamacoesParaContar = reclamacoes.Select(r =>
        new ReclamacaoParaContar(r.PedidoId, r.Quantidade, false, false, reclamacoesDispensadas.Contains(r.Id)));
    foreach (var (pedidoId, total) in ContarPorPedido(baixasParaContar, reclamacoesParaContar)) resultado[pedidoId] = total;
    return resultado;
  }

  /// <summary>
  /// Os campos de reposicao de cada baixa (chave: Id da baixa), em lote. <paramref name="linhasDoLivro"/> sao as linhas
  /// de todas as baixas (as `Perda` e os estornos delas) e <paramref name="nos"/> os nos delas, que o projetor da baixa
  /// ja leu: a unidade de Peca e a linha cujo no nao tem pai.
  /// </summary>
  public async Task<IReadOnlyDictionary<int, ReposicaoDaLinha>> PorBaixa(
      IReadOnlyList<Perda> perdas, IReadOnlyList<Movimentacao> linhasDoLivro, IReadOnlyDictionary<int, EstruturaItem> nos,
      CancellationToken ct)
  {
    if (perdas.Count == 0) return new Dictionary<int, ReposicaoDaLinha>();
    var dispensas = (await _reposicao.ListarDispensasVigentesAsync(perdas.Select(p => p.Id).ToList(), [], ct))
        .ToDictionary(d => d.PerdaId!.Value);
    var resumos = await ResumosAsync(
        perdas.Select(p => p.PedidoRetrabalhoId).OfType<int>(), dispensas.Values, ct);

    return perdas.ToDictionary(p => p.Id, p =>
    {
      var dispensa = dispensas.GetValueOrDefault(p.Id);
      var paraContar = ParaContar(p, linhasDoLivro, nos, dispensa is not null);
      return new ReposicaoDaLinha(
          p.PedidoRetrabalhoId is int r ? resumos.Retrabalhos.GetValueOrDefault(r) : null,
          dispensa is null ? null : resumos.Dispensas[dispensa.Id], UnidadesARepor(paraContar));
    });
  }

  /// <summary>Os campos de reposicao de cada reclamacao (chave: Id da reclamacao), em lote.</summary>
  public async Task<IReadOnlyDictionary<int, ReposicaoDaLinha>> PorReclamacao(
      IReadOnlyList<Reclamacao> reclamacoes, CancellationToken ct)
  {
    if (reclamacoes.Count == 0) return new Dictionary<int, ReposicaoDaLinha>();
    var dispensas = (await _reposicao.ListarDispensasVigentesAsync([], reclamacoes.Select(r => r.Id).ToList(), ct))
        .ToDictionary(d => d.ReclamacaoId!.Value);
    var resumos = await ResumosAsync(
        reclamacoes.Select(r => r.PedidoRetrabalhoId).OfType<int>(), dispensas.Values, ct);

    return reclamacoes.ToDictionary(r => r.Id, r =>
    {
      var dispensa = dispensas.GetValueOrDefault(r.Id);
      var paraContar = new ReclamacaoParaContar(
          r.PedidoId, r.Quantidade, r.EstornadaEm is not null, r.PedidoRetrabalhoId is not null, dispensa is not null);
      return new ReposicaoDaLinha(
          r.PedidoRetrabalhoId is int p ? resumos.Retrabalhos.GetValueOrDefault(p) : null,
          dispensa is null ? null : resumos.Dispensas[dispensa.Id], UnidadesARepor(paraContar));
    });
  }

  /// <summary>
  /// O numero do Pedido de origem de cada Retrabalho entre <paramref name="pedidos"/>, por Id do Pedido de origem, em
  /// uma consulta. Pedido de fabricacao nao tem origem e nao gera consulta.
  /// </summary>
  public async Task<IReadOnlyDictionary<int, string>> NumerosDasOrigensAsync(IEnumerable<Pedido> pedidos, CancellationToken ct)
  {
    var ids = pedidos.Select(p => p.PedidoOrigemId).OfType<int>().Distinct().ToList();
    return ids.Count == 0 ? new Dictionary<int, string>() : await _reposicao.ListarNumerosDePedidosAsync(ids, ct);
  }

  /// <summary>
  /// A dispensa como DTO, com o nome de quem a deu. Usada pelo "nao repor" ao devolver o `201`, no mesmo formato da
  /// baixa e da reclamacao.
  /// </summary>
  public async Task<DispensaDto> ProjetarDispensaAsync(DispensaDeReposicao dispensa, CancellationToken ct) =>
      (await ResumosAsync([], [dispensa], ct)).Dispensas[dispensa.Id];

  private static BaixaParaContar ParaContar(
      Perda p, IReadOnlyList<Movimentacao> linhasDoLivro, IReadOnlyDictionary<int, EstruturaItem> nos, bool dispensada)
  {
    var daBaixa = linhasDoLivro.Where(l => l.PerdaId == p.Id).ToList();
    var estornada = daBaixa.Any(l => l.Tipo == TiposDeMovimentacao.Estorno);
    // Uma unidade de Peca e uma linha `Perda` cujo no nao tem pai (C1): a perda de um Item so acende pela Peca do topo
    // que a mesma baixa leva (`incluirTopo`).
    var unidadesDePeca = daBaixa
        .Where(l => l.Tipo == TiposDeMovimentacao.Perda && nos.TryGetValue(l.EstruturaItemId, out var no) && no.EstruturaPaiId is null)
        .Sum(l => l.Quantidade);
    return new BaixaParaContar(p.PedidoId, p.Motivo, unidadesDePeca, estornada, p.PedidoRetrabalhoId is not null, dispensada);
  }

  private async Task<IReadOnlyDictionary<int, EstruturaItem>> NosDasLinhasAsync(IReadOnlyList<Movimentacao> linhas, CancellationToken ct)
  {
    var ids = linhas.Select(l => l.EstruturaItemId).Distinct().ToList();
    if (ids.Count == 0) return new Dictionary<int, EstruturaItem>();
    return (await _execucao.ListarNosAsync(ids, ct)).ToDictionary(n => n.Id);
  }

  private async Task<(IReadOnlyDictionary<int, PedidoRetrabalhoResumoDto> Retrabalhos, IReadOnlyDictionary<int, DispensaDto> Dispensas)>
      ResumosAsync(IEnumerable<int> retrabalhoIds, IEnumerable<DispensaDeReposicao> dispensas, CancellationToken ct)
  {
    var ids = retrabalhoIds.Distinct().ToList();
    IReadOnlyDictionary<int, string> numeros =
        ids.Count == 0 ? new Dictionary<int, string>() : await _reposicao.ListarNumerosDePedidosAsync(ids, ct);
    var lista = dispensas.ToList();
    IReadOnlyDictionary<int, string> autores = lista.Count == 0
        ? new Dictionary<int, string>()
        : await _execucao.ListarNomesDeUsuariosAsync(lista.Select(d => d.UsuarioId).Distinct().ToList(), ct);
    return (
        ids.Where(numeros.ContainsKey).ToDictionary(id => id, id => new PedidoRetrabalhoResumoDto(id, numeros[id])),
        lista.ToDictionary(d => d.Id, d => new DispensaDto(
            d.Id, d.Justificativa, d.DataHora, d.UsuarioId, autores.GetValueOrDefault(d.UsuarioId, string.Empty))));
  }
}

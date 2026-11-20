using Rastreamento.Application.Retrabalho;
using Rastreamento.Application.Separacao;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Andamento;

/// <summary>O que as complicacoes de C8 pedem de fora do livro: o resumo de material, os nomes e o "a repor" por Peca.</summary>
internal sealed record DadosDeComplicacao(
    IReadOnlyList<ResumoDoMaterial> Resumos, IReadOnlyDictionary<int, string> Descricoes, IReadOnlyDictionary<int, decimal> ARepor);

/// <summary>
/// A leitura em lote por tras das complicacoes (C8 da spec da Fase 6A), compartilhada pelo Panorama e pelo Andamento
/// do Pedido: o planejado, as linhas e os materiais de todos os nos de uma vez, e o "a repor" por Peca de uma consulta
/// so. A conta por Peca e a de <see cref="ComplicacoesDaPeca"/>.
/// </summary>
internal sealed class LeitorDeComplicacoes
{
  private readonly ISeparacaoRepository _separacao;
  private readonly ConsultaDeReposicao _reposicao;

  public LeitorDeComplicacoes(ISeparacaoRepository separacao, ConsultaDeReposicao reposicao)
  {
    _separacao = separacao;
    _reposicao = reposicao;
  }

  public async Task<DadosDeComplicacao> LerAsync(
      IReadOnlyCollection<int> todosOsNos, IReadOnlyCollection<int> pedidoIds, CancellationToken ct)
  {
    var planejado = await _separacao.ListarPlanejadoAsync(todosOsNos, ct);
    IReadOnlyList<ResumoDoMaterial> resumos = [];
    var descricoes = new Dictionary<int, string>();
    if (planejado.Count > 0)
    {
      var linhas = await _separacao.ListarLinhasAsync(todosOsNos, ct);
      var materiais = await _separacao.ListarMateriaisAsync(
          planejado.Select(p => p.MaterialId).Concat(linhas.Select(l => l.MaterialId)).Distinct().ToList(), ct);
      var doAlmoxarifado = materiais.Where(m => m.SetorDeEstoqueId is null).Select(m => m.Id).ToHashSet();
      descricoes = materiais.ToDictionary(m => m.Id, m => m.Descricao);
      resumos = CalculoDaSeparacao.Resumir(planejado, linhas, doAlmoxarifado).ToList();
    }

    var aRepor = await _reposicao.AReporPorPeca(pedidoIds.Distinct().ToList(), ct);
    return new DadosDeComplicacao(resumos, descricoes, aRepor);
  }
}

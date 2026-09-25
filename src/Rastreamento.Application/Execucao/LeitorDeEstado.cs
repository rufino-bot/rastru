using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>A calculadora montada sobre um conjunto de nos, e o nome de cada um para as frases.</summary>
internal sealed record EstadoDeExecucao(
    CalculadoraDeExecucao Calc, IReadOnlyDictionary<int, string> Descricoes, IReadOnlyDictionary<int, string?> Codigos)
{
  public string Nome(int id) => Descricoes.TryGetValue(id, out var descricao) ? descricao : $"no {id}";
}

/// <summary>
/// Le, para os nos pedidos, tudo o que a calculadora precisa — Roteiro, saldos, totais montados e passos
/// alcancados — em cinco consultas, nunca uma por no. A descricao segue a regra 19 (a do no, senao a do
/// Componente). Quem chama decide o conjunto: para o destino de um Item, o pai tem de estar nele.
/// </summary>
internal sealed class LeitorDeEstado
{
  private readonly IExecucaoRepository _execucao;
  private readonly IEstruturaRepository _estruturas;
  private readonly IReceitaPadraoRepository _catalogo;

  public LeitorDeEstado(IExecucaoRepository execucao, IEstruturaRepository estruturas, IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _estruturas = estruturas;
    _catalogo = catalogo;
  }

  public async Task<EstadoDeExecucao> CarregarAsync(IReadOnlyCollection<EstruturaItem> nos, CancellationToken ct)
  {
    var distintos = nos.DistinctBy(n => n.Id).ToList();
    var ids = distintos.Select(n => n.Id).ToList();

    var roteiros = (await _estruturas.ListarRoteiroAsync(ids, ct)).ToLookup(r => r.EstruturaItemId);
    var saldos = await _execucao.ListarSaldosAsync(ids, ct);
    var totais = await _execucao.ListarTotaisMontadosAsync(ids, ct);
    var alcancados = await _execucao.ListarPassosAlcancadosAsync(ids, ct);
    var componenteIds = distintos.Where(n => n.ComponenteId is not null).Select(n => n.ComponenteId!.Value).Distinct().ToList();
    var componentes = (await _catalogo.ObterComponentesPorIdAsync(componenteIds, ct)).ToDictionary(c => c.Id);

    var calc = new CalculadoraDeExecucao(
        distintos.Select(n => new NoDoCalculo(
            n.Id, n.EstruturaPaiId, n.Quantidade, n.QuantidadePorPai,
            roteiros[n.Id].OrderBy(r => r.Ordem).Select(r => new PassoDoCalculo(r.SetorId, r.Ordem)).ToList())),
        saldos, totais, alcancados);

    var descricoes = distintos.ToDictionary(
        n => n.Id,
        n => n.Descricao
             ?? (n.ComponenteId is int c && componentes.TryGetValue(c, out var comp) ? comp.Descricao : null)
             ?? $"no {n.Id}");
    var codigos = distintos.ToDictionary(
        n => n.Id,
        n => n.ComponenteId is int c && componentes.TryGetValue(c, out var comp) ? comp.Codigo : (string?)null);

    return new EstadoDeExecucao(calc, descricoes, codigos);
  }
}

using Rastreamento.Application.Tests.Estrutura;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Separacao;

/// <summary>
/// Fake em memoria de <see cref="ISeparacaoRepository"/>.
///
/// <para>
/// Dois modos, escolhidos pelo construtor. <b>Com o <see cref="FakeEstruturaRepo"/></b> (o que o
/// `CenarioDeExecucao` passa), o planejado e LIDO e ESCRITO pela lista `Materiais` dele: os testes da
/// Fila, da execucao e da separacao enxergam um planejado so, e um `SubstituirPlanejadoAsync` aparece
/// no que a Fila le. <see cref="Planejado"/> devolve essa mesma lista. <b>Sem ele</b>, o fake usa a
/// lista `Planejado` propria, que o teste arranja a mao.
/// </para>
///
/// <para>
/// `Nos` e o conjunto de nos que existem para <see cref="NoExisteAsync"/>; no modo com o
/// `FakeEstruturaRepo`, um no tambem existe quando esta em `Itens` dele. `Materiais` e o catalogo que
/// <see cref="ListarMateriaisAsync"/> devolve, por Id.
/// </para>
/// </summary>
public class FakeSeparacaoRepo : ISeparacaoRepository
{
  private readonly FakeEstruturaRepo? _estruturas;
  private readonly List<EstruturaMaterial> _planejadoProprio = [];
  private int _proximoIdDeLinha = 1;
  private int _proximoIdDePlanejado = 1;
  private bool _estornoConcorrenteArmado;

  public FakeSeparacaoRepo()
  {
  }

  /// <summary>Compartilha o planejado com `estruturas.Materiais` (ver o resumo da classe).</summary>
  public FakeSeparacaoRepo(FakeEstruturaRepo estruturas) => _estruturas = estruturas;

  /// <summary>O planejado em uso: o de `FakeEstruturaRepo.Materiais` quando compartilhado, o proprio senao.</summary>
  public List<EstruturaMaterial> Planejado => _estruturas?.Materiais ?? _planejadoProprio;

  public List<MaterialSeparacao> Linhas { get; } = [];
  public Dictionary<int, MaterialDaSeparacao> Materiais { get; } = [];
  public HashSet<int> Nos { get; } = [];

  /// <summary>Quantas vezes uma escrita do livro chegou (`GravarAsync` e `GravarEstornoAsync` com sucesso).</summary>
  public int Gravacoes { get; private set; }

  /// <summary>Faz a PROXIMA <see cref="GravarEstornoAsync"/> devolver `false` (outro estorno chegou antes), uma vez so.</summary>
  public bool EstornoConcorrenteNaProximaGravacao
  {
    get => _estornoConcorrenteArmado;
    set => _estornoConcorrenteArmado = value;
  }

  public Task<bool> NoExisteAsync(int estruturaItemId, CancellationToken ct) =>
      Task.FromResult(Nos.Contains(estruturaItemId) || (_estruturas?.Itens.Any(i => i.Id == estruturaItemId) ?? false));

  public Task<IReadOnlyList<EstruturaMaterial>> ListarPlanejadoAsync(
      IReadOnlyCollection<int> estruturaItemIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<EstruturaMaterial>>(
          Planejado.Where(m => estruturaItemIds.Contains(m.EstruturaItemId)).OrderBy(m => m.Id).ToList());

  public Task<IReadOnlyList<MaterialSeparacao>> ListarLinhasAsync(
      IReadOnlyCollection<int> estruturaItemIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<MaterialSeparacao>>(
          Linhas.Where(l => estruturaItemIds.Contains(l.EstruturaItemId)).OrderBy(l => l.Id).ToList());

  public Task<MaterialSeparacao?> ObterLinhaAsync(int id, CancellationToken ct) =>
      Task.FromResult(Linhas.SingleOrDefault(l => l.Id == id));

  public Task<bool> FoiEstornadaAsync(int separacaoId, CancellationToken ct) =>
      Task.FromResult(Linhas.Any(l => l.EstornoDeId == separacaoId));

  public Task<IReadOnlyList<MaterialDaSeparacao>> ListarMateriaisAsync(
      IReadOnlyCollection<int> materialIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<MaterialDaSeparacao>>(
          materialIds.Where(Materiais.ContainsKey).Distinct().OrderBy(id => id).Select(id => Materiais[id]).ToList());

  public Task<IReadOnlySet<int>> ListarNosComMaterialDoAlmoxarifadoAsync(
      IReadOnlyCollection<int> estruturaItemIds, CancellationToken ct)
  {
    var nos = Planejado
        .Where(m => estruturaItemIds.Contains(m.EstruturaItemId)
                    && Materiais.TryGetValue(m.MaterialId, out var material) && material.SetorDeEstoqueId is null)
        .Select(m => m.EstruturaItemId)
        .ToHashSet();
    return Task.FromResult<IReadOnlySet<int>>(nos);
  }

  public Task SubstituirPlanejadoAsync(
      int estruturaItemId, IReadOnlyList<(int MaterialId, decimal Quantidade)> linhas, CancellationToken ct)
  {
    // O piso vem da lista ANTES de apagar: um Id apagado nao volta (como o IDENTITY), e no modo
    // compartilhado a lista tambem guarda os Ids que a arvore deu.
    var piso = Planejado.Count == 0 ? 0 : Planejado.Max(m => m.Id);
    if (_proximoIdDePlanejado <= piso) _proximoIdDePlanejado = piso + 1;
    Planejado.RemoveAll(m => m.EstruturaItemId == estruturaItemId);
    var proximo = _proximoIdDePlanejado;
    foreach (var (materialId, quantidade) in linhas)
      Planejado.Add(new EstruturaMaterial
      {
        Id = proximo++, EstruturaItemId = estruturaItemId, MaterialId = materialId, Quantidade = quantidade,
      });
    _proximoIdDePlanejado = proximo;
    return Task.CompletedTask;
  }

  public Task GravarAsync(IReadOnlyList<MaterialSeparacao> linhas, CancellationToken ct)
  {
    if (linhas.Count == 0) return Task.CompletedTask;
    foreach (var linha in linhas)
    {
      linha.Id = ProximoIdDeLinha();
      Linhas.Add(linha);
    }
    Gravacoes++;
    return Task.CompletedTask;
  }

  public Task<bool> GravarEstornoAsync(MaterialSeparacao estorno, CancellationToken ct)
  {
    if (_estornoConcorrenteArmado)
    {
      _estornoConcorrenteArmado = false;
      return Task.FromResult(false);
    }

    // O indice unico do banco: um segundo estorno da mesma linha tambem perde.
    if (Linhas.Any(l => l.EstornoDeId is not null && l.EstornoDeId == estorno.EstornoDeId))
      return Task.FromResult(false);

    estorno.Id = ProximoIdDeLinha();
    Linhas.Add(estorno);
    Gravacoes++;
    return Task.FromResult(true);
  }

  private int ProximoIdDeLinha()
  {
    var piso = Linhas.Count == 0 ? 0 : Linhas.Max(l => l.Id);
    if (_proximoIdDeLinha <= piso) _proximoIdDeLinha = piso + 1;
    return _proximoIdDeLinha++;
  }
}

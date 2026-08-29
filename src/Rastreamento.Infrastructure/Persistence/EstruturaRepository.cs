using System.Data;
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class EstruturaRepository : IEstruturaRepository
{
  private readonly RastreamentoDbContext _db;

  public EstruturaRepository(RastreamentoDbContext db) => _db = db;

  /// <summary>Tabela inteira, sem `WHERE` — mesmo criterio de `ReceitaPadraoRepository.ListarTodasAsArestasAsync`: catalogo, nao producao.</summary>
  public async Task<(IReadOnlyList<(int Pai, int Filho, decimal Qtd)> Filhos,
        IReadOnlyList<(int Comp, int Material, decimal Qtd)> Materiais,
        IReadOnlyList<(int Comp, int Setor, int Ordem)> Roteiro)>
      LerReceitaCompletaAsync(CancellationToken ct)
  {
    var filhos = await _db.FilhosPadrao.AsNoTracking()
        .Select(f => new ValueTuple<int, int, decimal>(f.ComponentePaiId, f.ComponenteFilhoId, f.QuantidadePadrao))
        .ToListAsync(ct);
    var materiais = await _db.MateriaisPadrao.AsNoTracking()
        .Select(m => new ValueTuple<int, int, decimal>(m.ComponenteId, m.MaterialId, m.QuantidadePadrao))
        .ToListAsync(ct);
    var roteiro = await _db.RoteirosPadrao.AsNoTracking()
        .Select(r => new ValueTuple<int, int, int>(r.ComponenteId, r.SetorId, r.Ordem))
        .ToListAsync(ct);

    return (filhos, materiais, roteiro);
  }

  /// <summary>
  /// Transacao explicita, mesmo molde de `ReceitaPadraoRepository.Substituir`: pai antes de filho,
  /// porque o filho precisa do Id do pai em `EstruturaPaiId`. Devolve o Id da raiz — ver o XML doc
  /// de `IEstruturaRepository.GravarArvoreAsync` para o porque do desvio do `Task` do brief.
  ///
  /// Residual conhecido, e deliberadamente FORA do escopo desta task: ao contrario de
  /// `ReceitaPadraoRepository.Substituir`, este metodo NAO traduz deadlock/lock-timeout (1205/1222)
  /// do SERIALIZABLE em `ConflitoDeConcorrenciaException` — nenhum teste desta task cobre essa
  /// corrida, e a traducao sem teste que a mate seria guarda encenada. Se a Task 4/5 tocar
  /// concorrencia em `EstruturaItem`, considerar extrair o mesmo padrao.
  /// </summary>
  public async Task<int> GravarArvoreAsync(
      int agrupamentoId, int? estruturaPaiId, NoParaGravar raiz, CancellationToken ct)
  {
    await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    var itemRaiz = await GravarNo(agrupamentoId, estruturaPaiId, raiz, ct);
    await tx.CommitAsync(ct);
    return itemRaiz.Id;
  }

  private async Task<EstruturaItem> GravarNo(
      int agrupamentoId, int? paiId, NoParaGravar no, CancellationToken ct)
  {
    var item = new EstruturaItem
    {
      AgrupamentoId = agrupamentoId,
      ComponenteId = no.ComponenteId,
      Descricao = no.Descricao,
      EstruturaPaiId = paiId,
      NivelHierarquico = paiId is null ? "Peca" : "Item",
      Quantidade = no.Quantidade,
      RequerRelatorioDimensional = no.RequerRelatorioDimensional,
    };
    _db.Estruturas.Add(item);
    await _db.SaveChangesAsync(ct);   // obtem o Id, exigido pelas FKs de Material/Roteiro e dos filhos

    foreach (var material in no.Materiais)
      _db.EstruturaMateriais.Add(new EstruturaMaterial
      {
        EstruturaItemId = item.Id,
        MaterialId = material.MaterialId,
        Quantidade = material.Quantidade,
      });

    foreach (var passo in no.Roteiro)
      _db.EstruturaRoteiros.Add(new EstruturaRoteiro
      {
        EstruturaItemId = item.Id,
        SetorId = passo.SetorId,
        Ordem = passo.Ordem,
      });

    if (no.Materiais.Count > 0 || no.Roteiro.Count > 0)
      await _db.SaveChangesAsync(ct);

    foreach (var filho in no.Filhos)
      await GravarNo(agrupamentoId, item.Id, filho, ct);

    return item;
  }

  /// <summary>
  /// `OrderBy(Id)`: defesa em profundidade contra Minor 2 da review da Task 3 — sem `ORDER BY`, um
  /// heap sem indice clusterizado nao garante ordem estavel entre chamadas. Quem PROVA a
  /// ordenacao e o teste de Application (`MontagemDeEstruturaUseCase`, com o fake alimentado fora
  /// de ordem de proposito): esta tabela e pequena e um teste de Infra ficaria verde de qualquer
  /// jeito, mutante ou nao.
  /// </summary>
  public async Task<IReadOnlyList<EstruturaItem>> ListarDoAgrupamentoAsync(
      int agrupamentoId, CancellationToken ct) =>
      await _db.Estruturas.AsNoTracking().Where(e => e.AgrupamentoId == agrupamentoId)
          .OrderBy(e => e.Id).ToListAsync(ct);

  /// <summary>RASTREADA de proposito: a Task 4 edita/exclui a partir deste retorno.</summary>
  public Task<EstruturaItem?> ObterPorIdAsync(int id, CancellationToken ct) =>
      _db.Estruturas.SingleOrDefaultAsync(e => e.Id == id, ct);

  /// <summary>`OrderBy(Id)`: mesmo motivo de `ListarDoAgrupamentoAsync` acima.</summary>
  public async Task<IReadOnlyList<EstruturaMaterial>> ListarMateriaisAsync(
      IReadOnlyList<int> itemIds, CancellationToken ct) =>
      await _db.EstruturaMateriais.AsNoTracking()
          .Where(m => itemIds.Contains(m.EstruturaItemId)).OrderBy(m => m.Id).ToListAsync(ct);

  public async Task<IReadOnlyList<EstruturaRoteiro>> ListarRoteiroAsync(
      IReadOnlyList<int> itemIds, CancellationToken ct) =>
      await _db.EstruturaRoteiros.AsNoTracking()
          .Where(r => itemIds.Contains(r.EstruturaItemId)).OrderBy(r => r.Ordem).ToListAsync(ct);

  public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}

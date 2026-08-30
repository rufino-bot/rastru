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

  /// <summary>
  /// Transacao explicita, como `GravarArvoreAsync`, mas SEM `Serializable`: apagar nao disputa a
  /// mesma corrida de insercao concorrente que motivou o isolamento la (residual documentado
  /// naquele metodo, fora do escopo desta task). Reune a subarvore NIVEL POR NIVEL (largura),
  /// comecando no proprio `id`, para poder apagar do nivel mais profundo para o mais raso — a FK
  /// self-referenciada em `EstruturaPaiId` exige filho antes de pai. `EstruturaMaterial`/
  /// `EstruturaRoteiro` saem primeiro, de TODOS os nos da subarvore de uma vez (a FK deles e para o
  /// `EstruturaItemId`, entao nao competem com a ordem nivel-a-nivel dos proprios nos).
  ///
  /// Conjunto de `visitados` (Minor 6 da review da Task 4): `CK_EstruturaItem_NaoAutoReferencia` so
  /// impede um no apontar pra SI MESMO — nao impede um ciclo mais longo (A -&gt; B -&gt; A) entre
  /// nos distintos. Sem este conjunto, um ciclo nos dados travaria o `while` para sempre, com a
  /// transacao aberta segurando locks. Lanca <see cref="SubarvoreCiclicaException"/> em vez de
  /// travar — audivel, no mesmo espirito de `ArvoreInconsistenteException` (Minor 3 da Task 3).
  /// </summary>
  public async Task RemoverSubarvoreAsync(int id, CancellationToken ct)
  {
    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    var visitados = new HashSet<int> { id };
    var niveis = new List<List<int>> { new() { id } };
    while (true)
    {
      var nivelAtual = niveis[^1];
      var filhos = await _db.Estruturas.AsNoTracking()
          .Where(e => e.EstruturaPaiId != null && nivelAtual.Contains(e.EstruturaPaiId!.Value))
          .Select(e => e.Id)
          .ToListAsync(ct);
      if (filhos.Count == 0) break;

      foreach (var filhoId in filhos)
        if (!visitados.Add(filhoId))
          throw new SubarvoreCiclicaException(
              $"A subarvore a partir do no {id} tem um ciclo em EstruturaPaiId: o no {filhoId} "
                  + "reaparece como filho depois de ja ter sido visitado. Corrija os dados antes de "
                  + "excluir.");

      niveis.Add(filhos);
    }

    var todos = niveis.SelectMany(nivel => nivel).ToList();

    // M5 da review da Task 4: `ct` propagado e leitura assincrona nas tres consultas que antes
    // passavam um `IQueryable` direto a `RemoveRange` (que o enumera de forma SINCRONA e bloqueante
    // dentro de um metodo `async`, sem honrar o CancellationToken) — mesmo padrao `ToListAsync(ct)`
    // do resto do arquivo (`:20-28`, `:104`, `:114`, `:119`).
    _db.EstruturaMateriais.RemoveRange(
        await _db.EstruturaMateriais.Where(m => todos.Contains(m.EstruturaItemId)).ToListAsync(ct));
    _db.EstruturaRoteiros.RemoveRange(
        await _db.EstruturaRoteiros.Where(r => todos.Contains(r.EstruturaItemId)).ToListAsync(ct));
    await _db.SaveChangesAsync(ct);

    // Do nivel mais profundo para o mais raso: o proprio `id` (niveis[0]) e apagado por ultimo.
    for (var i = niveis.Count - 1; i >= 0; i--)
    {
      var idsDoNivel = niveis[i];
      _db.Estruturas.RemoveRange(await _db.Estruturas.Where(e => idsDoNivel.Contains(e.Id)).ToListAsync(ct));
      await _db.SaveChangesAsync(ct);
    }

    await tx.CommitAsync(ct);
  }
}

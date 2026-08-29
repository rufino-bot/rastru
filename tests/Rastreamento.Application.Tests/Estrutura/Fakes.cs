using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Estrutura;

/// <summary>Fake em memoria de <see cref="IEstruturaRepository"/>, no molde dos fakes de Cadastros/Fakes.cs.</summary>
public class FakeEstruturaRepo : IEstruturaRepository
{
  private int _proximoId = 1;

  public List<EstruturaItem> Itens { get; } = [];
  public List<EstruturaMaterial> Materiais { get; } = [];
  public List<EstruturaRoteiro> Roteiros { get; } = [];

  /// <summary>Arestas da receita padrao que `LerReceitaCompletaAsync` devolve — arranjo do teste.</summary>
  public List<(int Pai, int Filho, decimal Qtd)> ReceitaFilhos { get; } = [];
  public List<(int Comp, int Material, decimal Qtd)> ReceitaMateriais { get; } = [];
  public List<(int Comp, int Setor, int Ordem)> ReceitaRoteiro { get; } = [];

  /// <summary>Quantas vezes `GravarArvoreAsync` foi chamado — prova que o caminho de erro nao grava.</summary>
  public int GravacoesDeArvore { get; private set; }

  public int Saves { get; private set; }

  public Task<(IReadOnlyList<(int Pai, int Filho, decimal Qtd)> Filhos,
        IReadOnlyList<(int Comp, int Material, decimal Qtd)> Materiais,
        IReadOnlyList<(int Comp, int Setor, int Ordem)> Roteiro)>
      LerReceitaCompletaAsync(CancellationToken ct) =>
      Task.FromResult<(IReadOnlyList<(int, int, decimal)>, IReadOnlyList<(int, int, decimal)>, IReadOnlyList<(int, int, int)>)>(
          (ReceitaFilhos, ReceitaMateriais, ReceitaRoteiro));

  public Task<int> GravarArvoreAsync(
      int agrupamentoId, int? estruturaPaiId, NoParaGravar raiz, CancellationToken ct)
  {
    GravacoesDeArvore++;
    var item = GravarNo(agrupamentoId, estruturaPaiId, raiz);
    return Task.FromResult(item.Id);
  }

  private EstruturaItem GravarNo(int agrupamentoId, int? paiId, NoParaGravar no)
  {
    var item = new EstruturaItem
    {
      Id = _proximoId++,
      AgrupamentoId = agrupamentoId,
      ComponenteId = no.ComponenteId,
      Descricao = no.Descricao,
      EstruturaPaiId = paiId,
      NivelHierarquico = paiId is null ? "Peca" : "Item",
      Quantidade = no.Quantidade,
      RequerRelatorioDimensional = no.RequerRelatorioDimensional,
    };
    Itens.Add(item);

    foreach (var material in no.Materiais)
      Materiais.Add(new EstruturaMaterial
      {
        Id = _proximoId++,
        EstruturaItemId = item.Id,
        MaterialId = material.MaterialId,
        Quantidade = material.Quantidade,
      });

    foreach (var passo in no.Roteiro)
      Roteiros.Add(new EstruturaRoteiro
      {
        Id = _proximoId++,
        EstruturaItemId = item.Id,
        SetorId = passo.SetorId,
        Ordem = passo.Ordem,
      });

    foreach (var filho in no.Filhos)
      GravarNo(agrupamentoId, item.Id, filho);

    return item;
  }

  public Task<IReadOnlyList<EstruturaItem>> ListarDoAgrupamentoAsync(int agrupamentoId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<EstruturaItem>>(
          Itens.Where(i => i.AgrupamentoId == agrupamentoId).ToList());

  public Task<EstruturaItem?> ObterPorIdAsync(int id, CancellationToken ct) =>
      Task.FromResult(Itens.SingleOrDefault(i => i.Id == id));

  public Task<IReadOnlyList<EstruturaMaterial>> ListarMateriaisAsync(
      IReadOnlyList<int> itemIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<EstruturaMaterial>>(
          Materiais.Where(m => itemIds.Contains(m.EstruturaItemId)).ToList());

  public Task<IReadOnlyList<EstruturaRoteiro>> ListarRoteiroAsync(
      IReadOnlyList<int> itemIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<EstruturaRoteiro>>(
          Roteiros.Where(r => itemIds.Contains(r.EstruturaItemId)).OrderBy(r => r.Ordem).ToList());

  public Task SalvarAlteracoesAsync(CancellationToken ct)
  {
    Saves++;
    return Task.CompletedTask;
  }
}

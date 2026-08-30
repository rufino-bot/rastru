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
    // A Task 4 e a primeira a semear `Itens` DIRETO (para montar um no ja existente antes de
    // AcrescentarFilho/EditarNo/ExcluirNo) e DEPOIS gravar um no novo no mesmo teste — antes disso
    // nenhum teste combinava as duas coisas. Sem este ajuste, um teste que insere `Itens.Add(new
    // EstruturaItem { Id = 1, ... })` na mao colide com `_proximoId`, que continua nascendo em 1: o
    // no novo tambem vira Id=1, com `EstruturaPaiId` apontando pro Id que acabou de receber —
    // auto-referencia que estoura a pilha em `MontadorDeArvoreDeEstrutura.MontarAsync` (a recursao
    // nunca visita o mesmo Id duas vezes num Agrupamento bem formado, entao nao tem guarda de
    // ciclo). Reavaliar o piso a CADA chamada, nao so no construtor, porque o seeding acontece
    // depois de o Fake ja existir.
    var pisoDeItens = Itens.Count == 0 ? 0 : Itens.Max(i => i.Id);
    var pisoDeMateriais = Materiais.Count == 0 ? 0 : Materiais.Max(m => m.Id);
    var pisoDeRoteiros = Roteiros.Count == 0 ? 0 : Roteiros.Max(r => r.Id);
    var proximoMinimo = Math.Max(pisoDeItens, Math.Max(pisoDeMateriais, pisoDeRoteiros)) + 1;
    if (_proximoId < proximoMinimo) _proximoId = proximoMinimo;

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

  /// <summary>Mesma logica nivel-a-nivel do repositorio real, sem transacao (fake em memoria nao precisa).</summary>
  public Task RemoverSubarvoreAsync(int id, CancellationToken ct)
  {
    var idsParaRemover = new HashSet<int> { id };
    var fronteira = new List<int> { id };
    while (fronteira.Count > 0)
    {
      var filhos = Itens.Where(i => i.EstruturaPaiId.HasValue && fronteira.Contains(i.EstruturaPaiId.Value))
          .Select(i => i.Id).ToList();
      foreach (var filho in filhos) idsParaRemover.Add(filho);
      fronteira = filhos;
    }

    Materiais.RemoveAll(m => idsParaRemover.Contains(m.EstruturaItemId));
    Roteiros.RemoveAll(r => idsParaRemover.Contains(r.EstruturaItemId));
    Itens.RemoveAll(i => idsParaRemover.Contains(i.Id));
    return Task.CompletedTask;
  }
}

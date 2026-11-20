using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Andamento;

/// <summary>
/// O que conta como parado (C5 da spec da Fase 6A) e a arvore de cada Peca, compartilhados pelo Panorama e pelo
/// Andamento do Pedido: o "a iniciar" de um no com filhos nao conta (quem espera sao os filhos), e as posicoes
/// terminais nao guardam partes (o <see cref="CalculoDaIdade"/> ja as deixa de fora).
/// </summary>
internal static class PartesQueContam
{
  public static bool Conta(CalculadoraDeExecucao calc, int noId, Local onde) =>
      !(calc.TemFilhos(noId) && onde.Posicao == Posicoes.AIniciar);

  /// <summary>A chegada da parte mais antiga do no cujo lugar satisfaz `onde`, sem o "a iniciar" de no com filhos.</summary>
  public static DateTime? MaisAntiga(CalculadoraDeExecucao calc, IdadesDoLivro idades, int noId, Func<Local, bool> onde) =>
      calc.TemFilhos(noId) && onde(Local.AIniciar) ? null : idades.MaisAntiga(noId, onde);

  /// <summary>C5, C6: a menor chegada entre as partes que contam dos nos pedidos.</summary>
  public static DateTime? ParadoDesde(IEnumerable<int> nos, IdadesDoLivro idades, CalculadoraDeExecucao calc)
  {
    DateTime? menor = null;
    foreach (var id in nos)
      foreach (var parte in idades.Partes(id))
      {
        if (!Conta(calc, id, parte.Local)) continue;
        if (menor is null || parte.Chegada < menor) menor = parte.Chegada;
      }
    return menor;
  }

  /// <summary>Os nos de cada Peca pedida (ela e todos os descendentes), pela raiz de cada no, numa passada so.</summary>
  public static Dictionary<int, HashSet<int>> NosDeCadaPeca(IReadOnlyList<ContextoDoNo> todos, IReadOnlyCollection<int> pecaIds)
  {
    var pai = todos.ToDictionary(x => x.No.Id, x => x.No.EstruturaPaiId);
    int Raiz(int id)
    {
      while (pai.TryGetValue(id, out var p) && p is int acima) id = acima;
      return id;
    }

    var porPeca = pecaIds.ToDictionary(id => id, id => new HashSet<int> { id });
    foreach (var x in todos)
      if (x.No.EstruturaPaiId is not null && porPeca.TryGetValue(Raiz(x.No.Id), out var nos)) nos.Add(x.No.Id);
    return porPeca;
  }
}

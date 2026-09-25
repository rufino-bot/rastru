using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// A soma do livro em C#: por movimento, +Quantidade no destino e -Quantidade na origem, agrupado por
/// no e posicao. E a MESMA conta que `ExecucaoRepository.ListarSaldosAsync` faz em SQL (spec secao
/// 7.1), e existe para duas coisas: o fake de teste somar igual ao banco, e
/// `Saldos_do_banco_batem_com_a_soma_em_CSharp` provar que as duas somam igual. O estorno nao tem
/// filtro especial: e um movimento inverso, e a soma o absorve.
/// </summary>
public static class Livro
{
  public static IReadOnlyList<SaldoLiquido> SomarSaldos(IEnumerable<Movimentacao> movimentos)
  {
    var soma = new Dictionary<(int Item, Local Local), decimal>();
    foreach (var m in movimentos)
    {
      Somar(soma, (m.EstruturaItemId, Local.DoDestino(m)), m.Quantidade);
      Somar(soma, (m.EstruturaItemId, Local.DaOrigem(m)), -m.Quantidade);
    }

    return soma
        .Where(kv => kv.Value != 0m)
        .OrderBy(kv => kv.Key.Item)
        .ThenBy(kv => OrdemDaPosicao(kv.Key.Local.Posicao))
        .ThenBy(kv => kv.Key.Local.Ordem ?? 0)
        .ThenBy(kv => kv.Key.Local.SetorId ?? 0)
        .Select(kv => new SaldoLiquido(
            kv.Key.Item, kv.Key.Local.Posicao, kv.Key.Local.SetorId, kv.Key.Local.Ordem, kv.Value))
        .ToList();
  }

  /// <summary>A posicao na ordem de `Posicoes.Todas` — a ordem em que as telas a listam.</summary>
  public static int OrdemDaPosicao(string posicao)
  {
    for (var i = 0; i < Posicoes.Todas.Count; i++)
      if (Posicoes.Todas[i] == posicao) return i;
    return int.MaxValue;
  }

  private static void Somar(Dictionary<(int Item, Local Local), decimal> soma, (int Item, Local Local) chave, decimal valor) =>
      soma[chave] = soma.GetValueOrDefault(chave) + valor;
}

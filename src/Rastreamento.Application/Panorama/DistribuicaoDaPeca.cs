using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Panorama;

/// <summary>Os tipos de segmento do "Onde esta" (C8 da spec do Panorama).</summary>
public static class TiposDeSegmento
{
  public const string AIniciar = "AIniciar";
  public const string Passo = "Passo";         // um por passo do Roteiro: NoSetor + AguardandoColeta daquele passo
  public const string Conserto = "Conserto";   // um por Setor: as tres posicoes do conserto somadas
  public const string Expedicao = "Expedicao"; // as cinco posicoes do local de expedicao somadas
  public const string Expedido = "Expedido";
  public const string Perdido = "Perdido";
}

/// <summary>
/// Um segmento do "Onde esta" (C8). `AguardandoColeta` so no segmento de passo; zero nos outros. `Desde` e
/// `AguardandoColetaDesde` sao as chegadas mais antigas (C6 da spec da Fase 6A), nulas sem `maisAntiga`.
/// </summary>
public sealed record Segmento(
    string Tipo, int? SetorId, int? Ordem, decimal Quantidade, decimal AguardandoColeta,
    DateTime? Desde = null, DateTime? AguardandoColetaDesde = null);

public static class DistribuicaoDaPeca
{
  /// <summary>
  /// Os saldos da Peca (os de `CalculadoraDeExecucao.Saldos`) na ordem de C8, sem segmento com zero. A ordem daquela
  /// calculadora e por tipo de posicao, nao por passo, entao e montada aqui. `AguardandoMontagem` e `Montado` nao
  /// ocorrem numa Peca (so filho vai a montagem) e sao ignoradas. `maisAntiga` devolve a chegada da parte mais antiga
  /// dos lugares que satisfazem o filtro (nulo sem nenhuma, e nulo para o "a iniciar" de no com filhos, C5): sem ela,
  /// as datas saem nulas.
  /// </summary>
  public static IReadOnlyList<Segmento> Montar(
      IReadOnlyList<(Local Local, decimal Quantidade)> saldos, Func<Func<Local, bool>, DateTime?>? maisAntiga = null)
  {
    DateTime? Desde(Func<Local, bool> onde) => maisAntiga?.Invoke(onde);

    var r = new List<Segmento>();

    void Acrescentar(Segmento s)
    {
      if (s.Quantidade != 0) r.Add(s);
    }

    Acrescentar(new(TiposDeSegmento.AIniciar, null, null, Soma(saldos, l => l.Posicao == Posicoes.AIniciar), 0m,
        Desde(l => l.Posicao == Posicoes.AIniciar)));

    var passos = saldos
        .Where(s => s.Local.Posicao is Posicoes.NoSetor or Posicoes.AguardandoColeta)
        .GroupBy(s => (SetorId: s.Local.SetorId!.Value, Ordem: s.Local.Ordem!.Value))
        .OrderBy(g => g.Key.Ordem).ThenBy(g => g.Key.SetorId);
    foreach (var g in passos)
    {
      var aguardando = g.Where(s => s.Local.Posicao == Posicoes.AguardandoColeta).Sum(s => s.Quantidade);
      var (setorId, ordem) = g.Key;
      Acrescentar(new(
          TiposDeSegmento.Passo, setorId, ordem, g.Sum(s => s.Quantidade), aguardando,
          Desde(l => l.Posicao == Posicoes.NoSetor && l.SetorId == setorId && l.Ordem == ordem),
          Desde(l => l.Posicao == Posicoes.AguardandoColeta && l.SetorId == setorId && l.Ordem == ordem)));
    }

    var consertos = saldos
        .Where(s => Posicoes.DoConserto.Contains(s.Local.Posicao))
        .GroupBy(s => s.Local.SetorId!.Value)
        .OrderBy(g => g.Key);
    foreach (var g in consertos)
      Acrescentar(new(
          TiposDeSegmento.Conserto, g.Key, null, g.Sum(s => s.Quantidade), 0m,
          Desde(l => Posicoes.DoConserto.Contains(l.Posicao) && l.SetorId == g.Key)));

    Acrescentar(new(TiposDeSegmento.Expedicao, null, null, Soma(saldos, l => Posicoes.NoLocalDeExpedicao.Contains(l.Posicao)), 0m,
        Desde(l => Posicoes.NoLocalDeExpedicao.Contains(l.Posicao))));
    Acrescentar(new(TiposDeSegmento.Expedido, null, null, Soma(saldos, l => l.Posicao == Posicoes.Expedido), 0m));
    Acrescentar(new(TiposDeSegmento.Perdido, null, null, Soma(saldos, l => l.Posicao == Posicoes.Perdido), 0m));
    return r;
  }

  private static decimal Soma(IReadOnlyList<(Local Local, decimal Quantidade)> saldos, Func<Local, bool> quais) =>
      saldos.Where(s => quais(s.Local)).Sum(s => s.Quantidade);
}

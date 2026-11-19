using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Panorama;

/// <summary>
/// O "Embalado" de uma Peca, calculado na leitura (regra 40): `Vale` quando a marca e a ultima, a condicao de C18
/// vale agora e nada voltou a producao depois dela; `PodeMarcar` e a condicao de C18; `EmProducao` e a quantidade nas
/// sete posicoes de producao.
/// </summary>
public sealed record EstadoDaEmbalagem(bool Vale, bool PodeMarcar, decimal EmProducao);

public static class CalculoDaEmbalagem
{
  /// <summary>
  /// As sete posicoes de C18 — NAO e `CalculadoraDeExecucao.EmProducao`, que conta o local de expedicao como em
  /// producao e por isso nunca deixaria a Peca pronta ser marcada.
  /// </summary>
  public static readonly IReadOnlySet<string> PosicoesDeProducao = new HashSet<string>
  {
    Posicoes.AIniciar, Posicoes.NoSetor, Posicoes.AguardandoColeta, Posicoes.AguardandoMontagem,
    Posicoes.AguardandoConserto, Posicoes.EmConserto, Posicoes.ConsertoPronto,
  };

  public static EstadoDaEmbalagem Estado(IReadOnlyList<(Local Local, decimal Quantidade)> saldos, UltimaEmbalagem? ultima)
  {
    var emProducao = saldos.Where(s => PosicoesDeProducao.Contains(s.Local.Posicao)).Sum(s => s.Quantidade);
    var noLocal = saldos.Where(s => Posicoes.NoLocalDeExpedicao.Contains(s.Local.Posicao)).Sum(s => s.Quantidade);

    var podeMarcar = emProducao == 0 && noLocal > 0;
    var vale = ultima is { Tipo: TiposDeEmbalagem.Marcar, VoltouAProducaoDepois: false } && podeMarcar;
    return new EstadoDaEmbalagem(vale, podeMarcar, emProducao);
  }
}

using Rastreamento.Application.Common;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// Regra 25 na entrega (spec da Fase 3B, secao 4.3). Os itens cujo destino e `AguardandoMontagem` de um pai que
/// recebe em conjunto (`RecebeEmConjunto`) sao agrupados pelo pai, e cada grupo tem de trazer TODOS os filhos
/// diretos, com o mesmo numero inteiro de conjuntos, sem passar do teto. Valida com as mesmas funcoes que as
/// Tarefas mostram (`TetoDeEntrada`), e roda depois de o destino de cada item estar calculado.
/// </summary>
internal static class ConjuntoCompleto
{
  public sealed record ItemDestinado(int EstruturaItemId, Local Origem, Local Destino, decimal Quantidade);

  public sealed record Recusa(string Codigo, TipoDeErro Tipo, string Mensagem);

  public static Recusa? Conferir(EstadoDeExecucao estado, IReadOnlyList<ItemDestinado> itens)
  {
    var calc = estado.Calc;
    var porPai = itens
        .Where(i => i.Destino.Posicao == Posicoes.AguardandoMontagem
                    && calc.No(i.EstruturaItemId).PaiId is int paiId && calc.RecebeEmConjunto(paiId))
        .GroupBy(i => calc.No(i.EstruturaItemId).PaiId!.Value)
        .OrderBy(g => g.Key);

    foreach (var grupo in porPai)
    {
      var paiId = grupo.Key;
      var pai = estado.Nome(paiId);
      var somas = grupo.GroupBy(i => i.EstruturaItemId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantidade));

      decimal? conjuntos = null;
      foreach (var filho in calc.Filhos(paiId))
      {
        var nome = estado.Nome(filho.Id);
        if (!somas.TryGetValue(filho.Id, out var soma))
          return Incompleto($"Falta {nome} no conjunto de {pai}: um Kit só entra com todos os filhos juntos.");

        var razao = filho.QuantidadePorPai ?? 0m;
        var destes = razao > 0m ? soma / razao : 0m;
        if (destes < 1m || destes != decimal.Truncate(destes))
          return Incompleto($"{Quantidades.Formatar(soma)} de {nome} não fecha conjuntos completos de {pai}: "
              + $"cada conjunto leva {Quantidades.Formatar(razao)}.");
        if (conjuntos is decimal anteriores && anteriores != destes)
          return Incompleto($"Os filhos de {pai} não formam o mesmo número de conjuntos: {nome} dá "
              + $"{Quantidades.Formatar(destes)}, os anteriores dão {Quantidades.Formatar(anteriores)}.");
        conjuntos = destes;
      }

      // No redirecionamento, o que sai de `AguardandoMontagem` de um Setor com `UtilizaKit` ja estava contado na
      // espera: sai da conta, para nao ocupar o teto duas vezes (D5 da spec da Fase 3B).
      var saindo = grupo
          .Where(i => i.Origem.Posicao == Posicoes.AguardandoMontagem && calc.SetorUtilizaKit(i.Origem.SetorId!.Value))
          .GroupBy(i => i.EstruturaItemId)
          .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantidade));
      var teto = Math.Floor(calc.TetoDeEntrada(paiId, saindo));
      if (conjuntos > teto)
        return new Recusa(CodigosDaExecucao.AlemDoQueOPaiPrecisa, TipoDeErro.Conflito,
            $"{pai} só precisa receber {Quantidades.Formatar(teto)} conjunto(s); a entrega leva {Quantidades.Formatar(conjuntos!.Value)}.");
    }
    return null;
  }

  private static Recusa Incompleto(string mensagem) =>
      new(CodigosDaExecucao.ConjuntoIncompleto, TipoDeErro.Validacao, mensagem);
}

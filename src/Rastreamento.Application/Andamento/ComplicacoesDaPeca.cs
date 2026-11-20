using Rastreamento.Application.Execucao;
using Rastreamento.Application.Separacao;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Andamento;

public sealed record ComplicacaoDto(string Codigo, string Texto);

/// <summary>Os codigos das complicacoes de uma Peca (C8 da spec da Fase 6A), na ordem fixa em que aparecem.</summary>
public static class CodigosDeComplicacao
{
  public const string MaterialPendente = "MaterialPendente";
  public const string AguardandoLiberacao = "AguardandoLiberacao";
  public const string AguardandoDecisao = "AguardandoDecisao";
  public const string EmConserto = "EmConserto";
  public const string MontagemIncompleta = "MontagemIncompleta";
  public const string ARepor = "ARepor";

  public static readonly IReadOnlyList<string> Todos =
  [
    MaterialPendente, AguardandoLiberacao, AguardandoDecisao, EmConserto, MontagemIncompleta, ARepor,
  ];
}

/// <summary>
/// O que esta travando uma Peca (C8 da spec da Fase 6A), em funcao pura sobre a calculadora que a tela ja usa.
/// No maximo uma complicacao por codigo, na ordem de <see cref="CodigosDeComplicacao.Todos"/>.
/// </summary>
public static class ComplicacoesDaPeca
{
  public static IReadOnlyList<ComplicacaoDto> Calcular(
      IReadOnlyCollection<int> nosDaPeca,
      CalculadoraDeExecucao calc,
      IReadOnlyList<ResumoDoMaterial> materiais,
      IReadOnlyDictionary<int, string> descricoesDosMateriais,
      decimal aRepor,
      Func<int, string> nomeDoNo)
  {
    var nos = nosDaPeca.OrderBy(id => id).ToList();
    var resultado = new List<ComplicacaoDto>();

    var pendentes = materiais
        .Where(m => nosDaPeca.Contains(m.EstruturaItemId) && m.Pendencia > 0m
            && calc.Saldo(m.EstruturaItemId, Local.AIniciar) > 0m)
        .OrderBy(m => m.EstruturaItemId).ThenBy(m => m.MaterialId)
        .ToList();
    if (pendentes.Count > 0)
    {
      var primeiro = descricoesDosMateriais.GetValueOrDefault(pendentes[0].MaterialId) ?? $"#{pendentes[0].MaterialId}";
      var texto = $"Material não separado: {primeiro}";
      if (pendentes.Count > 1) texto += $" e mais {pendentes.Count - 1}";
      resultado.Add(new ComplicacaoDto(CodigosDeComplicacao.MaterialPendente, texto));
    }

    var liberacao = nos.Sum(id => calc.Saldo(id, Local.AguardandoLiberacao));
    if (liberacao > 0m)
      resultado.Add(new ComplicacaoDto(
          CodigosDeComplicacao.AguardandoLiberacao, $"Aguardando liberação da Gestão ({Quantidades.Formatar(liberacao)})"));

    var decisao = nos.Sum(id => calc.Saldo(id, Local.NaoConforme));
    if (decisao > 0m)
      resultado.Add(new ComplicacaoDto(
          CodigosDeComplicacao.AguardandoDecisao, $"Não conformes aguardando decisão ({Quantidades.Formatar(decisao)})"));

    var conserto = nos.Sum(id => calc.Saldos(id)
        .Where(s => Posicoes.DoConserto.Contains(s.Local.Posicao) && s.Quantidade > 0m).Sum(s => s.Quantidade));
    if (conserto > 0m)
      resultado.Add(new ComplicacaoDto(CodigosDeComplicacao.EmConserto, $"Em conserto ({Quantidades.Formatar(conserto)})"));

    var montagem = MontagemIncompleta(nos, calc, nomeDoNo);
    if (montagem is not null) resultado.Add(montagem);

    if (aRepor > 0m)
      resultado.Add(new ComplicacaoDto(CodigosDeComplicacao.ARepor, $"{Quantidades.Formatar(aRepor)} a repor"));

    return resultado;
  }

  /// <summary>
  /// O primeiro pai (menor Id) com filho aguardando montagem num Setor em que a montagem do pai ainda nao fecha a
  /// proxima unidade: diz o que falta do filho de menor Id com `FaltaParaProxima` positiva.
  /// </summary>
  private static ComplicacaoDto? MontagemIncompleta(
      IReadOnlyList<int> nos, CalculadoraDeExecucao calc, Func<int, string> nomeDoNo)
  {
    foreach (var paiId in nos)
    {
      var filhos = calc.FilhosQueContam(paiId);
      var setores = filhos.SelectMany(f => calc.SetoresOndeAguardaMontagem(f.Id)).Distinct().OrderBy(s => s);
      foreach (var setorId in setores)
      {
        var m = calc.CalcularMontabilidade(paiId, setorId);
        var falta = m.Filhos.Where(f => f.FaltaParaProxima > 0m).OrderBy(f => f.FilhoId).FirstOrDefault();
        if (falta is not null)
          return new ComplicacaoDto(
              CodigosDeComplicacao.MontagemIncompleta,
              $"Montagem: faltam {Quantidades.Formatar(falta.FaltaParaProxima!.Value)} de {nomeDoNo(falta.FilhoId)}");
      }
    }
    return null;
  }
}

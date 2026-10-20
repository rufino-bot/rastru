using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Separacao;

/// <summary>
/// O que a regra 34 diz de um (no, material). `Pendencia` so existe para o material do almoxarifado que
/// esta no planejado (D6 da spec da Fase 4): o que fica num Setor o operador pega ali, e o que saiu do
/// plano nao tem mais o que faltar. `AcimaDoPlanejado` e aviso, nao recusa (D9), e tambem so existe para o
/// material no planejado: o que saiu do plano com historico aparece so como "fora do planejado".
/// </summary>
public sealed record ResumoDoMaterial(
    int EstruturaItemId, int MaterialId, decimal Planejado, decimal Separado, bool NoPlanejado, bool DoAlmoxarifado)
{
  public decimal Pendencia => DoAlmoxarifado && NoPlanejado ? Math.Max(0m, Planejado - Separado) : 0m;
  public bool AcimaDoPlanejado => NoPlanejado && Separado > Planejado;
}

/// <summary>
/// A regra 34 num lugar so (spec da Fase 4, secao 5.1): a mesma conta serve o painel do no, a lista do
/// Separador e o resumo da Fila, para nao ser reescrita em tres consultas. Puro: quem le o banco e o caso
/// de uso.
/// </summary>
public static class CalculoDaSeparacao
{
  /// <summary>Separacoes menos estornos, de UM no e UM material.</summary>
  public static decimal Separado(IEnumerable<MaterialSeparacao> linhas) =>
      linhas.Sum(l => l.Tipo == TiposDeSeparacao.Estorno ? -l.Quantidade : l.Quantidade);

  /// <summary>
  /// Um <see cref="ResumoDoMaterial"/> por (no, material) que esta no planejado OU tem linha no livro.
  /// Material fora do planejado com linha: `NoPlanejado = false`, `Planejado = 0` (o historico continua a
  /// vista depois de o PCP tirar o material do plano). Ordem: no, depois `MaterialId`.
  /// </summary>
  public static IReadOnlyList<ResumoDoMaterial> Resumir(
      IEnumerable<EstruturaMaterial> planejado, IEnumerable<MaterialSeparacao> linhas,
      IReadOnlySet<int> materiaisDoAlmoxarifado)
  {
    // `UQ_EstruturaMaterial` garante um planejado por (no, material).
    var planejadoPorChave = planejado.ToDictionary(p => (p.EstruturaItemId, p.MaterialId), p => p.Quantidade);
    var separadoPorChave = linhas
        .GroupBy(l => (l.EstruturaItemId, l.MaterialId))
        .ToDictionary(g => g.Key, Separado);

    return planejadoPorChave.Keys
        .Union(separadoPorChave.Keys)
        .OrderBy(k => k.EstruturaItemId).ThenBy(k => k.MaterialId)
        .Select(k =>
        {
          var noPlanejado = planejadoPorChave.TryGetValue(k, out var quantidadePlanejada);
          return new ResumoDoMaterial(
              k.EstruturaItemId, k.MaterialId,
              noPlanejado ? quantidadePlanejada : 0m,
              separadoPorChave.GetValueOrDefault(k),
              noPlanejado,
              materiaisDoAlmoxarifado.Contains(k.MaterialId));
        })
        .ToList();
  }

  /// <summary>
  /// O resumo da Fila de UM no: so os materiais no planejado E do almoxarifado, contando quantos estao
  /// completos (sem pendencia), nao quantidades. Nulo quando nao ha nenhum.
  /// </summary>
  public static ResumoDeMaterialDto? ResumoDaFila(IEnumerable<ResumoDoMaterial> doNo)
  {
    var contam = doNo.Where(r => r.NoPlanejado && r.DoAlmoxarifado).ToList();
    return contam.Count == 0
        ? null
        : new ResumoDeMaterialDto(contam.Count(r => r.Pendencia == 0m), contam.Count);
  }
}

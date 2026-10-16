using Rastreamento.Application.Cadastros;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Separacao;

/// <summary>
/// Do <see cref="ResumoDoMaterial"/> (a conta) e do <see cref="MaterialDaSeparacao"/> (o catalogo) para o
/// item de material que a API devolve. Num lugar so porque o painel do no, a resposta do `POST` de
/// separacao e a do estorno mostram o mesmo item.
/// </summary>
internal static class ProjecaoDaSeparacao
{
  public static MaterialDoNoComSeparacaoDto Projetar(ResumoDoMaterial r, MaterialDaSeparacao m) => new(
      m.Id, m.Codigo, m.Descricao, m.UnidadeMedida,
      m.SetorDeEstoqueId is { } setorId ? new SetorDeEstoqueDto(setorId, m.SetorDeEstoqueNome ?? string.Empty) : null,
      r.Planejado, r.Separado, r.Pendencia, r.NoPlanejado, r.AcimaDoPlanejado);
}

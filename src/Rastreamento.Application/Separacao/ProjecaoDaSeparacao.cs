using Rastreamento.Application.Cadastros;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Separacao;

/// <summary>
/// Do <see cref="ResumoDoMaterial"/> (a conta) e do <see cref="MaterialDaSeparacao"/> (o catalogo) para o
/// item de material que a API devolve. Num lugar so porque o painel do no, a resposta do `POST` de
/// separacao e a do estorno mostram o mesmo item. E da linha do livro para o <see cref="SeparacaoDto"/>,
/// que a resposta do `POST`, a do estorno e o historico mostram igual.
/// </summary>
internal static class ProjecaoDaSeparacao
{
  public static MaterialDoNoComSeparacaoDto Projetar(ResumoDoMaterial r, MaterialDaSeparacao m) => new(
      m.Id, m.Codigo, m.Descricao, m.UnidadeMedida,
      m.SetorDeEstoqueId is { } setorId ? new SetorDeEstoqueDto(setorId, m.SetorDeEstoqueNome ?? string.Empty) : null,
      r.Planejado, r.Separado, r.Pendencia, r.NoPlanejado, r.AcimaDoPlanejado);

  /// <summary>`estornavel` e decisao de quem chama: depende do leitor (autor, PCP) e de a linha ja ter estorno.</summary>
  public static SeparacaoDto Linha(MaterialSeparacao l, MaterialDaSeparacao m, string usuarioNome, bool estornavel) => new(
      l.Id, l.Tipo, m.Id, m.Codigo, m.Descricao, m.UnidadeMedida, l.Quantidade, l.DataHora, l.UsuarioId, usuarioNome,
      l.EstornoDeId, estornavel);
}

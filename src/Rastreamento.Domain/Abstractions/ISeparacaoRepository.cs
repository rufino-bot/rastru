using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Um Material como a separacao o mostra: com onde ele fica e o nome do Setor (inativo inclusive —
/// o nome continua sendo o de onde o material esta, mesmo com o Setor desativado).
/// </summary>
public sealed record MaterialDaSeparacao(
    int Id, string Codigo, string Descricao, string UnidadeMedida, bool Ativo, int? SetorDeEstoqueId, string? SetorDeEstoqueNome);

/// <summary>
/// Leituras e escritas da separacao de material (Fase 4). Sem transacao explicita, por decisao P1 do
/// plano de backend da Fase 4: separar grava todas as linhas num `SaveChanges` so, o `PUT` dos
/// materiais apaga e insere num `SaveChanges` so, e a corrida de estorno e fechada pelo indice unico
/// `UX_MaterialSeparacao_EstornoDe`, traduzido por <see cref="GravarEstornoAsync"/>.
/// </summary>
public interface ISeparacaoRepository
{
  Task<bool> NoExisteAsync(int estruturaItemId, CancellationToken ct);

  /// <summary>O planejado (`EstruturaMaterial`) dos nos pedidos. Sem rastreamento.</summary>
  Task<IReadOnlyList<EstruturaMaterial>> ListarPlanejadoAsync(IReadOnlyCollection<int> estruturaItemIds, CancellationToken ct);

  /// <summary>As linhas do livro (Separacao e Estorno) dos nos pedidos, por Id crescente. Sem rastreamento.</summary>
  Task<IReadOnlyList<MaterialSeparacao>> ListarLinhasAsync(IReadOnlyCollection<int> estruturaItemIds, CancellationToken ct);

  Task<MaterialSeparacao?> ObterLinhaAsync(int id, CancellationToken ct);

  /// <summary>Se existe um Estorno apontando a linha.</summary>
  Task<bool> FoiEstornadaAsync(int separacaoId, CancellationToken ct);

  Task<IReadOnlyList<MaterialDaSeparacao>> ListarMateriaisAsync(IReadOnlyCollection<int> materialIds, CancellationToken ct);

  /// <summary>Dos nos pedidos, os que tem ao menos um material do almoxarifado (SetorDeEstoqueId nulo) no planejado.</summary>
  Task<IReadOnlySet<int>> ListarNosComMaterialDoAlmoxarifadoAsync(IReadOnlyCollection<int> estruturaItemIds, CancellationToken ct);

  /// <summary>Apaga o planejado do no e grava `linhas`, num SaveChanges so.</summary>
  Task SubstituirPlanejadoAsync(int estruturaItemId, IReadOnlyList<(int MaterialId, decimal Quantidade)> linhas, CancellationToken ct);

  /// <summary>Grava as separacoes num SaveChanges so (tudo ou nada) e preenche os Ids.</summary>
  Task GravarAsync(IReadOnlyList<MaterialSeparacao> linhas, CancellationToken ct);

  /// <summary>Grava o estorno; false quando outro estorno da mesma linha chegou antes (`UX_MaterialSeparacao_EstornoDe`).</summary>
  Task<bool> GravarEstornoAsync(MaterialSeparacao estorno, CancellationToken ct);
}

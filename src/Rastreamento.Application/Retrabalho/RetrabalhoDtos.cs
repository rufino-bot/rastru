namespace Rastreamento.Application.Retrabalho;

/// <summary>
/// Um no da arvore real da Peca de origem, como o caso de uso o le para copiar (secao 4.3 da spec da
/// Fase 5B-2). <c>Id</c> e o do no original: a copia o guarda em <c>OrigemEstruturaItemId</c>.
/// </summary>
public sealed record NoDaCopia(
    int Id,
    int? PaiId,
    int? ComponenteId,
    string? Descricao,
    decimal Quantidade,
    decimal? QuantidadePorPai,
    bool RequerRelatorioDimensional,
    IReadOnlyList<(int MaterialId, decimal Quantidade)> Materiais,
    IReadOnlyList<(int SetorId, int Ordem)> Roteiro);

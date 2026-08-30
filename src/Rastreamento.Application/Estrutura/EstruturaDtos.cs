namespace Rastreamento.Application.Estrutura;

public sealed record NovaPecaDto(int ComponenteId, decimal Quantidade, bool RequerRelatorioDimensional);

/// <summary>
/// `Descricao` ja vem RESOLVIDA: `EstruturaItem.Descricao` quando nao-nula, senao a descricao do
/// `Componente` (regra 19). O front nao faz esse fallback — se fizesse, cada consumidor novo teria
/// de lembrar dele.
/// </summary>
public sealed record EstruturaItemDto(
    int Id, int? ComponenteId, string? CodigoDoComponente, string Descricao,
    decimal Quantidade, string NivelHierarquico, bool RequerRelatorioDimensional,
    IReadOnlyList<MaterialDoNoDto> Materiais, IReadOnlyList<PassoDoRoteiroDto> Roteiro,
    IReadOnlyList<EstruturaItemDto> Filhos);

public sealed record MaterialDoNoDto(int MaterialId, string Nome, decimal Quantidade);

public sealed record PassoDoRoteiroDto(int SetorId, string Nome, int Ordem);

/// <summary>`ComponenteId` nulo = no ad-hoc, e ai `Descricao` e OBRIGATORIA (regra 19).</summary>
public sealed record NovoFilhoDto(int? ComponenteId, string? Descricao, decimal Quantidade);

/// <summary>
/// `Descricao` vazia/so espaco grava `null` (volta a herdar do Componente, regra 19 — exceto num
/// no ad-hoc, que nao tem Componente para herdar dela e recusa a edicao). `Quantidade` NAO
/// cascateia para os filhos ao editar (decisao de dominio — ver `MontagemDeEstruturaUseCase.EditarNo`).
/// </summary>
public sealed record EdicaoDeNoDto(string? Descricao, decimal Quantidade);

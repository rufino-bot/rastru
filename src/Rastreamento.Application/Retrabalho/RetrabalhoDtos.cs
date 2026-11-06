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

/// <summary>O Retrabalho que atende uma baixa ou uma reclamacao, em resumo: o Id e o numero do Pedido.</summary>
public sealed record PedidoRetrabalhoResumoDto(int Id, string Numero);

/// <summary>
/// A dispensa de reposicao (spec da Fase 5B-2, secao 3.4): a justificativa de nao repor, e quem a deu.
/// </summary>
public sealed record DispensaDto(int Id, string Justificativa, DateTime DataHora, int UsuarioId, string UsuarioNome);

/// <summary>O corpo de `POST /pedidos/{id}/reclamacoes`: `Descricao` e o que o cliente disse.</summary>
public sealed record NovaReclamacaoDto(int EstruturaItemId, decimal Quantidade, string? Descricao);

/// <summary>
/// A reclamacao projetada (secao 4.7 da spec da Fase 5B-2). `Descricao` e o nome da Peca (regra 19) e `Codigo` o do
/// Componente; `Texto` e o que o cliente disse. `Estornavel`: nao estornada e de quem le, ou quem le ve as de todos.
/// `ARepor` conta unidades de Peca (C1): a quantidade da reclamacao enquanto vigente, zero estornada.
/// </summary>
public sealed record ReclamacaoDto(
    int Id, int PecaId, string Descricao, string? Codigo, decimal Quantidade, string Texto, DateTime DataHora,
    int UsuarioId, string UsuarioNome, bool Estornada, bool Estornavel, PedidoRetrabalhoResumoDto? PedidoRetrabalho,
    DispensaDto? Dispensa, decimal ARepor);

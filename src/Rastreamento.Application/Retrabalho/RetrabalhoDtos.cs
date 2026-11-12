using System.ComponentModel.DataAnnotations;

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

/// <summary>O corpo de `POST /baixas/{id}/dispensa` e de `POST /reclamacoes/{id}/dispensa`: a justificativa de nao repor (C15).</summary>
public sealed record NovaDispensaDto(string? Justificativa);

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

/// <summary>
/// O corpo de `POST /pedidos/{id}/retrabalhos` ({id} = o original; secao 4.2 da spec da Fase 5B-2). A porta e uma so:
/// `PerdaId` (da baixa), `ReclamacaoId` (da reclamacao, com `Destino` e, no `Consertar`, `SetorId`) ou nenhuma das duas
/// (avulsa, com `EstruturaItemId` de uma Peca do original e `Quantidade`). `DataEntrega` nula fica a do original (C7).
/// O `Numero` NAO leva `[MaxLength]`: o limite (30) e conferido no caso de uso, para a recusa sair com `erro` e `mensagem`
/// (400 `NumeroLongoDemais`) e nao no `ProblemDetails` automatico.
/// </summary>
public sealed record NovoRetrabalhoDto(
    int? PerdaId, int? ReclamacaoId, int? EstruturaItemId, decimal? Quantidade, string? Destino, int? SetorId,
    string? Numero, DateOnly? DataEntrega,
    string? MotivoRetrabalho);

/// <summary>De onde veio o Retrabalho: `Baixa` ou `Reclamacao` (com o Id do alvo), ou `Avulsa` (sem Id).</summary>
public sealed record OrigemDoRetrabalhoDto(string Tipo, int? Id);

/// <summary>Um Retrabalho na lista do Pedido de origem.</summary>
public sealed record RetrabalhoResumoDto(
    int Id, string Numero, string? MotivoRetrabalho, string Status, DateOnly DataEntrega, OrigemDoRetrabalhoDto Origem);

/// <summary>
/// `GET /pedidos/{id}/retrabalhos`: os Retrabalhos do Pedido, do mais antigo ao mais novo, e o numero que o proximo
/// recebe por sugestao (decisao P7 do plano de backend da Fase 5B-2).
/// </summary>
public sealed record RetrabalhosDoPedidoDto(string NumeroSugerido, IReadOnlyList<RetrabalhoResumoDto> Retrabalhos);

/// <summary>
/// Uma linha da previa do "Usar a sobra" (secao 4.4 da spec da Fase 5B-2): a sobra viva do no de origem numa posicao,
/// oferecida ao no do Retrabalho que veio dele. `EstruturaItemId`, `Descricao` (regra 19) e `Codigo` sao do no do
/// Retrabalho; `Precisa` e o que ele ainda pode receber de pronto antes desta posicao, e `Sugerido` o minimo dos dois.
/// </summary>
public sealed record SobraReaproveitavelDto(
    int EstruturaItemId, string Descricao, string? Codigo, string Posicao, int? SetorId, string? SetorNome, int? Ordem,
    decimal Sobra, decimal Precisa, decimal Sugerido);

/// <summary>`GET /pedidos/{id}/sobra-reaproveitavel` ({id} = o Retrabalho), de cima para baixo.</summary>
public sealed record PreviaDoReaproveitamentoDto(IReadOnlyList<SobraReaproveitavelDto> Itens);

/// <summary>Uma linha do corpo do "Usar a sobra": tantas unidades da sobra naquela posicao viram pronto no no.</summary>
public sealed record ItemDoReaproveitamentoDto(int EstruturaItemId, string? Posicao, int? SetorId, int? Ordem, decimal Quantidade);

/// <summary>O corpo de `POST /pedidos/{id}/sobra-reaproveitada` ({id} = o Retrabalho).</summary>
public sealed record NovoReaproveitamentoDto(IReadOnlyList<ItemDoReaproveitamentoDto>? Itens);

using System.ComponentModel.DataAnnotations;
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Execucao;

namespace Rastreamento.Application.Separacao;

// ---------------------------------------------------------------------------
// Materiais do no (D8 da spec da Fase 4)
// ---------------------------------------------------------------------------

/// <summary>
/// Um material do no com a conta da regra 34. `FicaEm` nulo = almoxarifado. O material que saiu do plano
/// e tem separacao vem com `NoPlanejado = false` e `Planejado = 0`. `AcimaDoPlanejado` vai em todo item
/// (decisao P5 do plano de backend da Fase 4).
/// </summary>
public sealed record MaterialDoNoComSeparacaoDto(
    int MaterialId, string Codigo, string Descricao, string UnidadeMedida, SetorDeEstoqueDto? FicaEm,
    decimal Planejado, decimal Separado, decimal Pendencia, bool NoPlanejado, bool AcimaDoPlanejado);

/// <summary>Os materiais de um no, por codigo.</summary>
public sealed record MateriaisDoNoDto(int EstruturaItemId, IReadOnlyList<MaterialDoNoComSeparacaoDto> Materiais);

public sealed record LinhaDeMaterialDto(int MaterialId, decimal Quantidade);

/// <summary>Corpo do `PUT` dos materiais do no: substitui a lista inteira; lista vazia limpa.</summary>
/// <remarks>`[Required]` na lista anulavel pelo mesmo motivo do `ReceitaDeMateriaisDto`: `{}` vira 400, nao lista vazia.</remarks>
public sealed record MateriaisNovosDoNoDto([Required] IReadOnlyList<LinhaDeMaterialDto>? Linhas);

// ---------------------------------------------------------------------------
// Separacao (regra 34)
// ---------------------------------------------------------------------------

/// <summary>Corpo do `POST` de separacao: uma linha por material.</summary>
/// <remarks>`[Required]` na lista anulavel pelo mesmo motivo do `ReceitaDeMateriaisDto`: `{}` vira 400.</remarks>
public sealed record SeparacaoNovaDto([Required] IReadOnlyList<LinhaDeMaterialDto>? Linhas);

/// <summary>
/// Uma linha do livro de separacao. `Tipo` e um dos `TiposDeSeparacao`; `EstornoDeId` so no estorno.
/// `Estornavel` e calculado para quem pede, como os `estornaveis` da Fila (spec da Fase 4, secao 5.2): uma
/// `Separacao` ainda sem estorno, do proprio leitor ou de qualquer autor para o PCP e o Administrador.
/// </summary>
public sealed record SeparacaoDto(
    int Id, string Tipo, int MaterialId, string MaterialCodigo, string MaterialDescricao, string UnidadeMedida,
    decimal Quantidade, DateTime DataHora, int UsuarioId, string UsuarioNome, int? EstornoDeId, bool Estornavel);

/// <summary>Resposta do `POST` de separacao: as linhas gravadas e os materiais que elas tocaram.</summary>
public sealed record ResultadoDaSeparacaoDto(
    IReadOnlyList<SeparacaoDto> Separacoes, IReadOnlyList<MaterialDoNoComSeparacaoDto> Materiais);

// ---------------------------------------------------------------------------
// Resumo da Fila e lista do Separador
// ---------------------------------------------------------------------------

/// <summary>
/// O resumo de material de um no na Fila do Setor: quantos materiais do almoxarifado no planejado estao
/// completos, de quantos. Conta materiais, nao quantidades.
/// </summary>
public sealed record ResumoDeMaterialDto(int Completos, int Total);

public sealed record NoDaPendenciaDto(
    int Id, string Descricao, string? CodigoDoComponente, int AgrupamentoId, string AgrupamentoCodigo);

public sealed record PedidoDaPendenciaDto(
    int Id, string Numero, string Cliente, DateOnly DataEntrega, bool Atrasado, PausaResumoDto? Pausa);

public sealed record MaterialPendenteDto(
    int MaterialId, string Codigo, string Descricao, string UnidadeMedida, decimal Planejado, decimal Separado,
    decimal Pendencia);

/// <summary>
/// O Setor do primeiro passo do Roteiro do no, para onde o material vai (regra 5). Proprio, e nao o
/// `SetorResumoDto` (que sai `id`) nem o `SetorDeEstoqueDto` (onde o Material fica): o contrato da spec da
/// Fase 4, secao 5.2, pede `setorId`.
/// </summary>
public sealed record SetorDeDestinoDto(int SetorId, string Nome);

/// <summary>Um no com material pendente na lista do Separador. `Destino` nulo = no sem Roteiro.</summary>
public sealed record PendenciaDeSeparacaoDto(
    NoDaPendenciaDto No, PedidoDaPendenciaDto Pedido, SetorDeDestinoDto? Destino,
    IReadOnlyList<MaterialPendenteDto> Materiais);

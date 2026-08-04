using System.ComponentModel.DataAnnotations;

namespace Rastreamento.Application.Cadastros;

// ---------------------------------------------------------------------------
// Comum aos cadastros
// ---------------------------------------------------------------------------

/// <summary>
/// Detalhe do 409 de duplicidade. `ExisteInativo` e o que permite a tela oferecer "reativar o
/// existente" em vez de travar o usuario — indice UNIQUE nao filtrado nao perdoa nome repetido,
/// nem de linha inativa (ver a spec da Fase 1, secao "Politica de exclusao").
/// </summary>
public sealed record ValorDuplicadoDto(string Campo, bool ExisteInativo, int IdExistente);

/// <summary>Corpo de `PATCH /{recurso}/{id}/ativo`. Cobre inativar e reativar.</summary>
public sealed record DefinirAtivoDto(bool Ativo);

// ---------------------------------------------------------------------------
// Setor
// ---------------------------------------------------------------------------

public sealed record SetorDto(int Id, string Nome, bool Ativo);

/// <remarks>
/// `MaxLength` espelha o NVARCHAR(100) de `dbo.Setor.Nome`: nome longo demais vira 400 do proprio
/// ASP.NET (ValidationProblemDetails, por causa do [ApiController]), em vez de SqlException virando
/// 500. O atributo fica SEM alvo, ou seja, no parametro do construtor primario — e onde a validacao
/// de modelo do MVC le em record posicional. Com `[property:]` o MVC nem valida: ele lanca
/// InvalidOperationException ("validation metadata ... that will be ignored") e a requisicao vira
/// 500. Mesmo formato do `LoginBody` do AuthController. Nome so de espacos continua sendo regra do
/// use case: o atributo nao enxerga isso.
/// </remarks>
public sealed record NovoSetorDto([MaxLength(100)] string Nome);

// ---------------------------------------------------------------------------
// Material
// ---------------------------------------------------------------------------

public sealed record MaterialDto(
    int Id, string Codigo, string Descricao, string UnidadeMedida, bool Ativo);

/// <remarks>
/// Os `MaxLength` espelham `dbo.Material`: NVARCHAR(50), (200) e (10). Mesma regra de alvo do
/// `NovoSetorDto` — atributo SEM `[property:]`, no parametro do construtor primario, que e onde a
/// validacao de modelo do MVC le em record posicional. `UnidadeMedida` NAO ganha lista fechada
/// (enum / `[RegularExpression]`): o DDL cita `UN, M, KG, M2` como comentario, sem `CHECK`, e a
/// aplicacao nao inventa restricao que o schema nao tem. Campo so de espacos continua sendo regra
/// do use case: o atributo nao enxerga isso.
/// </remarks>
public sealed record NovoMaterialDto(
    [MaxLength(50)] string Codigo,
    [MaxLength(200)] string Descricao,
    [MaxLength(10)] string UnidadeMedida);

// ---------------------------------------------------------------------------
// Pedido
// ---------------------------------------------------------------------------

/// <remarks>
/// `DataAbertura` sai daqui em UTC; quem converte para GMT-3 e o HorarioDeBrasiliaJsonConverter,
/// registrado uma vez em Program.cs — nenhum endpoint precisa converter na mao.
/// </remarks>
public sealed record PedidoDto(
    int Id,
    string Numero,
    string Cliente,
    string Tipo,
    string Status,
    DateTime DataAbertura,
    int CriadoPorUsuarioId);

/// <remarks>
/// So `Numero` e `Cliente`: `Tipo` e `Status` sao decididos pelo use case, e o autor vem da claim
/// da sessao. Nenhum dos tres se aceita do cliente. Os `MaxLength` espelham `dbo.Pedido`.
/// </remarks>
public sealed record NovoPedidoDto(
    [MaxLength(30)] string Numero,
    [MaxLength(200)] string Cliente);

// ---------------------------------------------------------------------------
// Agrupamento
// ---------------------------------------------------------------------------

public sealed record AgrupamentoDto(
    int Id,
    int PedidoId,
    string Codigo,
    string Tipo,
    DateTime CriadoEm,
    int CriadoPorUsuarioId);

/// <remarks>
/// Sem `PedidoId`: ele vem da rota (`POST /pedidos/{pedidoId}/agrupamentos`), nao do corpo — assim
/// nao existe a possibilidade de os dois discordarem. O `MaxLength` espelha `dbo.Agrupamento`:
/// NVARCHAR(50). Mesma regra de alvo do `NovoSetorDto` — atributo SEM `[property:]`, no
/// parametro do construtor primario, que e onde a validacao de modelo do MVC le em record
/// posicional. `Tipo` NAO ganha `[RegularExpression]`: a lista fechada (Kit | Avulso) e regra do
/// use case, porque o CK_Agrupamento_Tipo do banco subiria como 500 em vez de 400.
/// </remarks>
public sealed record NovoAgrupamentoDto(
    [MaxLength(50)] string Codigo,
    [MaxLength(20)] string Tipo);

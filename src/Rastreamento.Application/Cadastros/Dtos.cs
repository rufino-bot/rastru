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
/// <remarks>
/// `bool?` + `[Required]`, e nao `bool`: value type nao-anulavel sempre tem valor depois do model
/// binding, entao o validador do [ApiController] nunca acusaria a AUSENCIA do campo e um corpo `{}`
/// vincularia `false` — o PATCH respondia 204 e inativava a linha em silencio. Como catalogo se
/// inativa em vez de se excluir, esse era o lado errado do trade-off. Com `bool?` o campo ausente
/// vira `null`, o `[Required]` transforma isso em 400 e o `corpo.Ativo!.Value` dos controllers so
/// roda depois de a validacao ter passado. Mesma regra de alvo do `NovoSetorDto`: atributo SEM
/// `[property:]`, no parametro do construtor primario — com `[property:]` o MVC nem valida, lanca
/// InvalidOperationException ("validation metadata ... that will be ignored") e a requisicao vira
/// 500. Um record so, compartilhado por Setor, Material e Componente: a correcao e molde-wide por
/// construcao.
/// </remarks>
public sealed record DefinirAtivoDto([Required] bool? Ativo);

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

// ---------------------------------------------------------------------------
// Componente
// ---------------------------------------------------------------------------

/// <remarks>
/// Sem `ArquivoSolido`/`ArquivoFoto`: as colunas existem em `dbo.Componente`, mas upload e a
/// regra 18 sao trabalho da Fase 2, e a entidade da 1B nao as mapeia.
/// </remarks>
public sealed record ComponenteDto(
    int Id, string Codigo, string Descricao, string Tipo, bool Ativo);

/// <remarks>
/// Os `MaxLength` espelham `dbo.Componente`: NVARCHAR(50), (200) e (20). Mesma regra de alvo do
/// `NovoSetorDto` — atributo SEM `[property:]`, no parametro do construtor primario, que e onde a
/// validacao de modelo do MVC le em record posicional. `Tipo` NAO ganha `[RegularExpression]`: a
/// lista fechada (Bruto | Fabricado | Montagem) e regra do use case, porque o CK_Componente_Tipo
/// do banco subiria como 500 em vez de 400. Campo so de espacos continua sendo regra do use case.
/// </remarks>
public sealed record NovoComponenteDto(
    [MaxLength(50)] string Codigo,
    [MaxLength(200)] string Descricao,
    [MaxLength(20)] string Tipo);

// ---------------------------------------------------------------------------
// Receita padrao do Componente (Fase 1C)
// ---------------------------------------------------------------------------

/// <summary>Uma linha de material-padrao, como ela sai na leitura (ja com dados do Material).</summary>
public sealed record MaterialPadraoDto(
    int Id,
    int MaterialId,
    string Codigo,
    string Descricao,
    string UnidadeMedida,
    decimal QuantidadePadrao);

/// <summary>Uma linha de material-padrao, como ela ENTRA. So id + quantidade.</summary>
public sealed record LinhaDeMaterialPadraoDto(int MaterialId, decimal QuantidadePadrao);

/// <remarks>
/// `[Required]` num `IReadOnlyList&lt;T&gt;?` ANULAVEL, e nao numa lista nao-anulavel: o
/// desserializador entrega `null` para campo ausente mesmo em propriedade nao-anulavel, e sem o
/// `[Required]` um corpo `{}` vincularia `Linhas = null`. Como lista VAZIA significa "apague a
/// receita" (§2.2 da spec), tratar `null` como vazio faria `POST {}` LIMPAR a receita em silencio —
/// a mesma classe de bug que o `DefinirAtivoDto` ja pagou com `bool?` (ver o remarks dele acima).
/// Com `[Required]`, campo ausente vira 400 do proprio [ApiController] e `[]` continua sendo o
/// comando explicito de apagar.
///
/// Atributo SEM `[property:]`, no parametro do construtor primario — e onde a validacao de modelo
/// do MVC le em record posicional. Com `[property:]` o MVC lanca InvalidOperationException e a
/// requisicao vira 500.
///
/// A prova de que `{}` vira 400 e de que `[]` apaga e de nivel HTTP: ela mora no teste de endpoint
/// da Task 6, nao aqui — este nivel nao roda model binding.
/// </remarks>
public sealed record ReceitaDeMateriaisDto([Required] IReadOnlyList<LinhaDeMaterialPadraoDto>? Linhas);

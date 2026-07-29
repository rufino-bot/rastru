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

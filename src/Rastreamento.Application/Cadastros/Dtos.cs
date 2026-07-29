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

// ---------------------------------------------------------------------------
// Setor
// ---------------------------------------------------------------------------

public sealed record SetorDto(int Id, string Nome, bool Ativo);

/// <remarks>
/// `MaxLength` espelha o NVARCHAR(100) de `dbo.Setor.Nome`: nome longo demais vira 400 do proprio
/// ASP.NET (ValidationProblemDetails, por causa do [ApiController]), em vez de SqlException virando
/// 500. O alvo `property:` e obrigatorio — em record posicional um atributo sem alvo pousa no
/// parametro, e a validacao de modelo le os atributos da PROPRIEDADE. Nome so de espacos continua
/// sendo regra do use case: o atributo nao enxerga isso.
/// </remarks>
public sealed record NovoSetorDto([property: MaxLength(100)] string Nome);

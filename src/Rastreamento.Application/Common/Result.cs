namespace Rastreamento.Application.Common;

/// <summary>
/// Natureza da falha, para o controller escolher o status HTTP sem comparar a mensagem de erro.
/// A Fase 0 so produz <see cref="NaoAutorizado"/>; as fases seguintes precisam distinguir "kit
/// inexistente" de "lote ja esta em outro setor" de "dimensional ja aprovado", e sem este canal
/// os controllers acabariam inventando comparacao de string ou migrando para excecoes.
/// </summary>
public enum TipoDeErro
{
  /// <summary>Entrada invalida — normalmente 400/422.</summary>
  Validacao = 0,

  /// <summary>O recurso referenciado nao existe — normalmente 404.</summary>
  NaoEncontrado,

  /// <summary>A operacao conflita com o estado atual — normalmente 409.</summary>
  Conflito,

  /// <summary>Credencial ou sessao invalida — normalmente 401.</summary>
  NaoAutorizado,
}

/// <summary>Resultado de um caso de uso que devolve valor.</summary>
public sealed class Result<T>
{
  public bool Sucesso { get; }
  public T? Valor { get; }
  public string? Erro { get; }

  /// <summary>
  /// Nulo por padrao — so faz sentido preencher em falha, e nem toda falha precisa dele. Existe
  /// para separar o CODIGO estavel que o front comuta (<see cref="Erro"/>, ex.: "CicloNaReceita")
  /// da FRASE legivel para o operador quando as duas divergem — ver
  /// `MontagemDeEstruturaUseCase.CriarPeca` (Important 2 da review da Task 3 da Fase 2), onde
  /// `Erro` carrega o codigo e `Detalhe` a frase que `PlanejadorDeCopia` ja produz. Aditivo de
  /// proposito: parametro opcional com default `null`, para nenhum call-site existente de
  /// <see cref="Falha"/> precisar mudar.
  /// </summary>
  public string? Detalhe { get; }

  /// <summary>Nulo quando <see cref="Sucesso"/> — so faz sentido em falha.</summary>
  public TipoDeErro? TipoDoErro { get; }

  private Result(bool sucesso, T? valor, string? erro, string? detalhe, TipoDeErro? tipoDoErro)
  {
    Sucesso = sucesso;
    Valor = valor;
    Erro = erro;
    Detalhe = detalhe;
    TipoDoErro = tipoDoErro;
  }

  public static Result<T> Ok(T valor) => new(true, valor, null, null, null);

  public static Result<T> Falha(string erro, TipoDeErro tipo = TipoDeErro.Validacao, string? detalhe = null) =>
      new(false, default, erro, detalhe, tipo);
}

/// <summary>
/// Resultado de um caso de uso sem valor de retorno. Existe para que o caso void nao precise
/// devolver <c>Task</c> pelado e ficar sem como sinalizar falha.
/// </summary>
public sealed class Result
{
  public bool Sucesso { get; }
  public string? Erro { get; }

  /// <summary>Ver o XML doc de <see cref="Result{T}.Detalhe"/> — mesmo papel, mesma aditividade.</summary>
  public string? Detalhe { get; }

  public TipoDeErro? TipoDoErro { get; }

  private Result(bool sucesso, string? erro, string? detalhe, TipoDeErro? tipoDoErro)
  {
    Sucesso = sucesso;
    Erro = erro;
    Detalhe = detalhe;
    TipoDoErro = tipoDoErro;
  }

  public static Result Ok() => new(true, null, null, null);

  public static Result Falha(string erro, TipoDeErro tipo = TipoDeErro.Validacao, string? detalhe = null) =>
      new(false, erro, detalhe, tipo);
}

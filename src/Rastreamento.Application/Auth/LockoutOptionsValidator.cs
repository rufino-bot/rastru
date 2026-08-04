using Microsoft.Extensions.Options;

namespace Rastreamento.Application.Auth;

/// <summary>
/// Valida a secao <c>Lockout</c> no startup (via <c>ValidateOnStart</c>), mesmo padrao do
/// <see cref="JwtOptionsValidator"/>. Valor nao positivo aqui nao explode: ele desfigura a defesa
/// em silencio (<c>MaxFalhas=0</c> tranca no primeiro erro de digitacao; <c>DuracaoMinutos=0</c> e
/// uma trava que ja nasce expirada). Configuracao errada tem que derrubar a aplicacao.
/// </summary>
public class LockoutOptionsValidator : IValidateOptions<LockoutOptions>
{
  public ValidateOptionsResult Validate(string? name, LockoutOptions options)
  {
    var falhas = new List<string>();

    if (options.MaxFalhas <= 0)
      falhas.Add($"Lockout:{nameof(LockoutOptions.MaxFalhas)} deve ser maior que zero.");

    if (options.DuracaoMinutos <= 0)
      falhas.Add($"Lockout:{nameof(LockoutOptions.DuracaoMinutos)} deve ser maior que zero.");

    return falhas.Count == 0
        ? ValidateOptionsResult.Success
        : ValidateOptionsResult.Fail(falhas);
  }
}

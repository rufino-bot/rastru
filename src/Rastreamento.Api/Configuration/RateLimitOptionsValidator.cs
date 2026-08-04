using Microsoft.Extensions.Options;

namespace Rastreamento.Api.Configuration;

/// <summary>
/// Valida a secao <c>RateLimit</c> no startup (via <c>ValidateOnStart</c>), mesmo padrao do
/// <c>JwtOptionsValidator</c>. Valor nao positivo aqui nao explode sozinho: <c>PermitLimit=0</c>
/// barra todo login e <c>WindowSeconds=0</c> e uma janela sem duracao — nos dois casos a API sobe
/// limpa e o login simplesmente para de funcionar.
/// </summary>
public class RateLimitOptionsValidator : IValidateOptions<RateLimitOptions>
{
  public ValidateOptionsResult Validate(string? name, RateLimitOptions options)
  {
    var falhas = new List<string>();

    if (options.PermitLimit <= 0)
      falhas.Add($"RateLimit:{nameof(RateLimitOptions.PermitLimit)} deve ser maior que zero.");

    if (options.WindowSeconds <= 0)
      falhas.Add($"RateLimit:{nameof(RateLimitOptions.WindowSeconds)} deve ser maior que zero.");

    return falhas.Count == 0
        ? ValidateOptionsResult.Success
        : ValidateOptionsResult.Fail(falhas);
  }
}

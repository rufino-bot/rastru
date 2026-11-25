using System.Net;
using Microsoft.Extensions.Options;

namespace Rastreamento.Api.Configuration;

/// <summary>
/// Valida a secao <c>ProxyReverso</c> no startup (via <c>ValidateOnStart</c>). Rede que nao e CIDR
/// derruba a aplicacao em vez de deixar o middleware sem rede alguma; prefixo zero (<c>0.0.0.0/0</c>,
/// <c>::/0</c>) tambem, porque confiaria em todo mundo — a "lista limpa" por outro caminho.
/// </summary>
public class ProxyReversoOptionsValidator : IValidateOptions<ProxyReversoOptions>
{
  public ValidateOptionsResult Validate(string? name, ProxyReversoOptions options)
  {
    if (string.IsNullOrWhiteSpace(options.Rede))
      return ValidateOptionsResult.Success;

    if (!IPNetwork.TryParse(options.Rede.Trim(), out var rede))
      return ValidateOptionsResult.Fail(
          $"ProxyReverso:{nameof(ProxyReversoOptions.Rede)} deve ser uma rede em CIDR (ex.: 172.30.0.0/24).");

    if (rede.PrefixLength == 0)
      return ValidateOptionsResult.Fail(
          $"ProxyReverso:{nameof(ProxyReversoOptions.Rede)} nao pode ter prefixo zero: confiaria em todo mundo.");

    return ValidateOptionsResult.Success;
  }
}

using System.Text;
using Microsoft.Extensions.Options;

namespace Rastreamento.Application.Auth;

/// <summary>
/// Valida a secao <c>Jwt</c> no startup (via <c>ValidateOnStart</c>). Sem isso, um deploy que
/// esqueca de sobrescrever o <c>appsettings.json</c> sobe limpo e assina tokens com a chave que
/// esta publica no repositorio: sem erro, sem log, sem sintoma ate alguem forjar um token.
/// Configuracao errada tem que derrubar a aplicacao, nao virar uma falha silenciosa de seguranca.
/// </summary>
public class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
  public ValidateOptionsResult Validate(string? name, JwtOptions options)
  {
    var falhas = new List<string>();

    if (string.IsNullOrWhiteSpace(options.Issuer))
      falhas.Add($"{nameof(JwtOptions.Issuer)} nao pode ser vazio.");

    if (string.IsNullOrWhiteSpace(options.Audience))
      falhas.Add($"{nameof(JwtOptions.Audience)} nao pode ser vazio.");

    if (string.IsNullOrWhiteSpace(options.SigningKey))
    {
      falhas.Add($"{nameof(JwtOptions.SigningKey)} nao pode ser vazia.");
    }
    // Trim() de proposito: a chave de exemplo com um \n ou espaco no fim (copiar/colar, ou um
    // gerador de template) passaria pela comparacao exata E pelo minimo de bytes, e a aplicacao
    // subiria com uma chave conhecida. A comparacao tem que ser sobre o valor efetivo.
    else if (options.SigningKey.Trim() == JwtOptions.SigningKeyPlaceholder.Trim())
    {
      falhas.Add(
          $"{nameof(JwtOptions.SigningKey)} ainda e a chave de exemplo commitada no " +
          "appsettings.json. Sobrescreva a configuracao no ambiente de destino.");
    }
    else if (Encoding.UTF8.GetByteCount(options.SigningKey) < JwtOptions.TamanhoMinimoDaSigningKeyEmBytes)
    {
      falhas.Add(
          $"{nameof(JwtOptions.SigningKey)} deve ter pelo menos " +
          $"{JwtOptions.TamanhoMinimoDaSigningKeyEmBytes} bytes (requisito do HMAC-SHA256).");
    }

    // Repetem a guarda do construtor do EmissorDeSessao de proposito: la a falha so aparece na
    // primeira resolucao do DI; aqui ela aparece no startup, antes de qualquer requisicao.
    if (options.AccessTokenMinutes <= 0)
      falhas.Add($"{nameof(JwtOptions.AccessTokenMinutes)} deve ser maior que zero.");

    if (options.RefreshTokenDays <= 0)
      falhas.Add($"{nameof(JwtOptions.RefreshTokenDays)} deve ser maior que zero.");

    return falhas.Count == 0
        ? ValidateOptionsResult.Success
        : ValidateOptionsResult.Fail(falhas);
  }
}

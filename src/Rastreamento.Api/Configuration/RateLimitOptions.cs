namespace Rastreamento.Api.Configuration;

/// <summary>
/// Politica de rate limit do <c>/auth/login</c> (secao <c>RateLimit</c> do appsettings). Fica na
/// camada Api, e nao na Application, porque e politica de transporte HTTP — nao regra de negocio
/// de autenticacao (essa e a <c>LockoutOptions</c>).
/// </summary>
public class RateLimitOptions
{
  /// <summary>
  /// Nome da politica aplicada ao <c>/auth/login</c> via <c>[EnableRateLimiting]</c>. Const para
  /// que o registro no <c>Program.cs</c> e o atributo no controller nao possam divergir.
  /// </summary>
  public const string NomeDaPoliticaDeLogin = "login";

  /// <summary>Tentativas permitidas por IP dentro da janela.</summary>
  public int PermitLimit { get; set; } = 10;

  /// <summary>Tamanho da janela fixa, em segundos.</summary>
  public int WindowSeconds { get; set; } = 60;
}

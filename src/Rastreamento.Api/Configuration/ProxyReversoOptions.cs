namespace Rastreamento.Api.Configuration;

/// <summary>
/// Proxy reverso na frente da API (secao <c>ProxyReverso</c>). So existe para dizer de quem o
/// <c>X-Forwarded-For</c> e o <c>X-Forwarded-Proto</c> sao confiaveis: sem isso o rate limit por IP
/// do <c>/auth/login</c> particiona pelo IP do proxy e vira global.
/// </summary>
public class ProxyReversoOptions
{
  /// <summary>
  /// Rede (CIDR, ex.: <c>172.30.0.0/24</c>) de onde vem o proxy; so cabecalho de origem dentro dela
  /// e honrado. Vazia ou ausente: o middleware nao e registrado e o cabecalho e ignorado.
  /// </summary>
  public string? Rede { get; set; }
}

namespace Rastreamento.Application.Auth;

/// <summary>
/// Politica de lockout de conta (secao <c>Lockout</c> do appsettings). E a trava FINA, por conta,
/// que complementa o rate limit (teto grosso, por IP): uma nao substitui a outra.
/// </summary>
public class LockoutOptions
{
  /// <summary>Falhas de senha em sequencia ate a conta ser trancada.</summary>
  public int MaxFalhas { get; set; } = 5;

  /// <summary>
  /// Duracao da trava, em minutos. E temporaria de proposito: expira sozinha, sem intervencao de
  /// admin. Assim, um atacante que tranque um operador so atrasa o acesso dele por esse tempo —
  /// o lockout-DoS fica limitado e aceito.
  /// </summary>
  public int DuracaoMinutos { get; set; } = 15;
}

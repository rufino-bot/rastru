namespace Rastreamento.Domain.Entities;

public class Usuario
{
  public int Id { get; set; }
  public string NomeUsuario { get; set; } = string.Empty;
  public string SenhaHash { get; set; } = string.Empty;
  public string NomeCompleto { get; set; } = string.Empty;
  public int PerfilId { get; set; }
  public bool Ativo { get; set; }

  /// <summary>Falhas de senha em sequencia desde o ultimo sucesso ou desde a ultima trava.</summary>
  public int FalhasConsecutivas { get; set; }

  /// <summary>
  /// Ate quando a conta esta trancada, em UTC. <c>null</c> ou no passado = destrancada — a trava
  /// expira sozinha, sem intervencao de admin (ver <c>LockoutOptions.DuracaoMinutos</c>).
  /// </summary>
  public DateTime? BloqueadoAte { get; set; }

  public Perfil Perfil { get; set; } = null!;
}

namespace Rastreamento.Domain.Entities;

public class RefreshToken
{
  public int Id { get; set; }
  public int UsuarioId { get; set; }
  public string TokenHash { get; set; } = string.Empty;
  public DateTime ExpiraEm { get; set; }
  public DateTime CriadoEm { get; set; }
  public DateTime? RevogadoEm { get; set; }
  public string? SubstituidoPorTokenHash { get; set; }
  public byte[] RowVersion { get; set; } = [];
  public Usuario Usuario { get; set; } = null!;
}

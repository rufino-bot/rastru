namespace Rastreamento.Domain.Entities;

public class Usuario
{
    public int Id { get; set; }
    public string NomeUsuario { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;
    public string NomeCompleto { get; set; } = string.Empty;
    public int PerfilId { get; set; }
    public bool Ativo { get; set; }
    public Perfil Perfil { get; set; } = null!;
}

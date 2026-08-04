using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
  public void Configure(EntityTypeBuilder<Usuario> b)
  {
    b.ToTable("Usuario");
    b.HasKey(u => u.Id);
    b.Property(u => u.NomeUsuario).HasMaxLength(50).IsRequired();
    b.Property(u => u.SenhaHash).HasMaxLength(200).IsRequired();
    b.Property(u => u.NomeCompleto).HasMaxLength(200).IsRequired();
    b.HasOne(u => u.Perfil).WithMany().HasForeignKey(u => u.PerfilId)
        .OnDelete(DeleteBehavior.NoAction);
  }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class PerfilConfiguration : IEntityTypeConfiguration<Perfil>
{
    public void Configure(EntityTypeBuilder<Perfil> b)
    {
        b.ToTable("Perfil");
        b.HasKey(p => p.Id);
        b.Property(p => p.Nome).HasMaxLength(30).IsRequired();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class ComponenteConfiguration : IEntityTypeConfiguration<Componente>
{
  public void Configure(EntityTypeBuilder<Componente> b)
  {
    b.ToTable("Componente");
    b.HasKey(c => c.Id);
    b.Property(c => c.Codigo).HasMaxLength(50).IsRequired();
    b.Property(c => c.Descricao).HasMaxLength(200).IsRequired();
    b.Property(c => c.Tipo).HasMaxLength(20).IsRequired();
    // Sem HasDefaultValue para Ativo: Database First — o default vive so no .sql.
  }
}

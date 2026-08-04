using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
  public void Configure(EntityTypeBuilder<Material> b)
  {
    b.ToTable("Material");
    b.HasKey(m => m.Id);
    b.Property(m => m.Codigo).HasMaxLength(50).IsRequired();
    b.Property(m => m.Descricao).HasMaxLength(200).IsRequired();
    b.Property(m => m.UnidadeMedida).HasMaxLength(10).IsRequired();
    // Sem HasDefaultValue para Ativo: Database First — o default vive so no .sql.
  }
}

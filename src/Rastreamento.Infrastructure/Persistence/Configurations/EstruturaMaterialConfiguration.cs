using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class EstruturaMaterialConfiguration : IEntityTypeConfiguration<EstruturaMaterial>
{
  public void Configure(EntityTypeBuilder<EstruturaMaterial> b)
  {
    b.ToTable("EstruturaMaterial");
    b.HasKey(x => x.Id);
    b.Property(x => x.Quantidade).HasPrecision(18, 4);
  }
}

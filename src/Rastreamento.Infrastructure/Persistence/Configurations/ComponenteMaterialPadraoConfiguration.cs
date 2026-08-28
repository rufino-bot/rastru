using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class ComponenteMaterialPadraoConfiguration
    : IEntityTypeConfiguration<ComponenteMaterialPadrao>
{
  public void Configure(EntityTypeBuilder<ComponenteMaterialPadrao> b)
  {
    b.ToTable("ComponenteMaterialPadrao");
    b.HasKey(x => x.Id);
    b.Property(x => x.QuantidadePadrao).HasPrecision(18, 4);
  }
}

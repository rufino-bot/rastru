using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class ComponenteFilhoPadraoConfiguration : IEntityTypeConfiguration<ComponenteFilhoPadrao>
{
  public void Configure(EntityTypeBuilder<ComponenteFilhoPadrao> b)
  {
    b.ToTable("ComponenteFilhoPadrao");
    b.HasKey(x => x.Id);
    // Espelha DECIMAL(18,4) do .sql. Sem isto o EF usa o default dele e trunca em silencio.
    b.Property(x => x.QuantidadePadrao).HasPrecision(18, 4);
  }
}

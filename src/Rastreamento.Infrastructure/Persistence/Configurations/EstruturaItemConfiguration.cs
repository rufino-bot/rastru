using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class EstruturaItemConfiguration : IEntityTypeConfiguration<EstruturaItem>
{
  public void Configure(EntityTypeBuilder<EstruturaItem> b)
  {
    b.ToTable("EstruturaItem");
    b.HasKey(x => x.Id);
    b.Property(x => x.Descricao).HasMaxLength(200);
    b.Property(x => x.NivelHierarquico).HasMaxLength(10).IsRequired();
    // Espelha DECIMAL(18,4) do .sql. Sem isto o EF usa o default dele e trunca em silencio.
    b.Property(x => x.Quantidade).HasPrecision(18, 4);
  }
}

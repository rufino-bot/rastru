using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class ComponenteRoteiroPadraoConfiguration
    : IEntityTypeConfiguration<ComponenteRoteiroPadrao>
{
  public void Configure(EntityTypeBuilder<ComponenteRoteiroPadrao> b)
  {
    b.ToTable("ComponenteRoteiroPadrao");
    b.HasKey(x => x.Id);
    // Sem HasPrecision: Ordem e INT. Sem HasDefaultValue: Database First, default so no .sql.
  }
}

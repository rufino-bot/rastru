using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class MontagemConfiguration : IEntityTypeConfiguration<Montagem>
{
  public void Configure(EntityTypeBuilder<Montagem> b)
  {
    b.ToTable("Montagem");
    b.HasKey(x => x.Id);
    b.Property(x => x.Quantidade).HasPrecision(18, 4);
  }
}

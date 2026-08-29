using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class EstruturaRoteiroConfiguration : IEntityTypeConfiguration<EstruturaRoteiro>
{
  public void Configure(EntityTypeBuilder<EstruturaRoteiro> b)
  {
    b.ToTable("EstruturaRoteiro");
    b.HasKey(x => x.Id);
  }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class AgrupamentoConfiguration : IEntityTypeConfiguration<Agrupamento>
{
    public void Configure(EntityTypeBuilder<Agrupamento> b)
    {
        b.ToTable("Agrupamento");
        b.HasKey(a => a.Id);
        b.Property(a => a.Codigo).HasMaxLength(50).IsRequired();
        b.Property(a => a.Tipo).HasMaxLength(20).IsRequired();
    }
}

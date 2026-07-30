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
        // DECIMAL(18,4) explicito: sem isso o EF assume decimal(18,2) e trunca a quarta casa em
        // silencio — o que na Fase 3 desalinharia a conservacao de quantidade.
        b.Property(a => a.Quantidade).HasPrecision(18, 4);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class PedidoPausaConfiguration : IEntityTypeConfiguration<PedidoPausa>
{
  public void Configure(EntityTypeBuilder<PedidoPausa> b)
  {
    b.ToTable("PedidoPausa");
    b.HasKey(x => x.Id);
    b.Property(x => x.Motivo).HasMaxLength(200);
    // Mesmo motivo de MontagemConfiguration: sem isto, um caso de uso que esquecer PausadoEm grava
    // 0001-01-01 e DF_PedidoPausa_PausadoEm nunca dispara.
    b.Property(x => x.PausadoEm).ValueGeneratedOnAdd();
  }
}

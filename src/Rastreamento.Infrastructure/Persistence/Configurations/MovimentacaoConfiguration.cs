using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class MovimentacaoConfiguration : IEntityTypeConfiguration<Movimentacao>
{
  public void Configure(EntityTypeBuilder<Movimentacao> b)
  {
    b.ToTable("Movimentacao");
    b.HasKey(x => x.Id);
    b.Property(x => x.Tipo).HasMaxLength(20).IsRequired();
    b.Property(x => x.OrigemPosicao).HasMaxLength(20).IsRequired();
    b.Property(x => x.DestinoPosicao).HasMaxLength(20).IsRequired();
    // Espelha DECIMAL(18,4) do .sql. Sem isto o EF usa o default dele e trunca em silencio.
    b.Property(x => x.Quantidade).HasPrecision(18, 4);
  }
}

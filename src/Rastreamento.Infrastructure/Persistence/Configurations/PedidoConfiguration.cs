using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class PedidoConfiguration : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> b)
    {
        b.ToTable("Pedido");
        b.HasKey(p => p.Id);
        b.Property(p => p.Numero).HasMaxLength(30).IsRequired();
        b.Property(p => p.Cliente).HasMaxLength(200).IsRequired();
        b.Property(p => p.Tipo).HasMaxLength(20).IsRequired();
        b.Property(p => p.MotivoRetrabalho).HasMaxLength(30);
        b.Property(p => p.Status).HasMaxLength(20).IsRequired();
        // Sem HasDefaultValue: Database First — os DEFAULT vivem so no .sql, e o use case e quem
        // define Status, Tipo e DataAbertura no insert.
    }
}

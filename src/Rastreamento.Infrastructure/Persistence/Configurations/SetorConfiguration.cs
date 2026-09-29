using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class SetorConfiguration : IEntityTypeConfiguration<Setor>
{
  public void Configure(EntityTypeBuilder<Setor> b)
  {
    b.ToTable("Setor");
    b.HasKey(s => s.Id);
    b.Property(s => s.Nome).HasMaxLength(100).IsRequired();
    b.Property(s => s.Atividade).HasMaxLength(40);
    // Sem HasDefaultValue para Ativo: Database First — o default vive so no .sql.
  }
}

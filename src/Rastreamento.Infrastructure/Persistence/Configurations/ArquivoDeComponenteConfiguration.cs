using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class ArquivoDeComponenteConfiguration : IEntityTypeConfiguration<ArquivoDeComponente>
{
  public void Configure(EntityTypeBuilder<ArquivoDeComponente> b)
  {
    b.ToTable("ArquivoDeComponente");
    b.HasKey(a => a.Id);
    b.Property(a => a.NomeOriginal).HasMaxLength(260).IsRequired();
    b.Property(a => a.Conteudo).IsRequired();
    b.Property(a => a.Sha256).HasColumnType("binary(32)").IsRequired();
    // CriadoEm e gravado pelo DEFAULT do banco: sem isto o EF manda o default do DateTime.
    b.Property(a => a.CriadoEm).ValueGeneratedOnAdd();
    // Sem HasDefaultValue: Database First — o default vive so no .sql.
  }
}

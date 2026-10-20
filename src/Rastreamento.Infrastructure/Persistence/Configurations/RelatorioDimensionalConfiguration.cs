using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class RelatorioDimensionalConfiguration : IEntityTypeConfiguration<RelatorioDimensional>
{
  public void Configure(EntityTypeBuilder<RelatorioDimensional> b)
  {
    b.ToTable("RelatorioDimensional");
    b.HasKey(x => x.Id);
    b.Property(x => x.Tipo).HasMaxLength(20).IsRequired();
    b.Property(x => x.Resultado).HasMaxLength(20);
    // Mesmo motivo de MovimentacaoConfiguration: sem ValueGeneratedOnAdd() o EF sempre manda o valor
    // CLR e o DEFAULT do banco (DF_RelatorioDimensional_DataHora) nunca dispara como rede de seguranca.
    b.Property(x => x.DataHora).ValueGeneratedOnAdd();
  }
}

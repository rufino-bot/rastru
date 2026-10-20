using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class RemessaConfiguration : IEntityTypeConfiguration<Remessa>
{
  public void Configure(EntityTypeBuilder<Remessa> b)
  {
    b.ToTable("Remessa");
    b.HasKey(x => x.Id);
    // Mesmo motivo de MovimentacaoConfiguration: sem ValueGeneratedOnAdd() o EF sempre manda o valor
    // CLR e o DEFAULT do banco (DF_Remessa_DataHora) nunca dispara como rede de seguranca.
    b.Property(x => x.DataHora).ValueGeneratedOnAdd();
  }
}

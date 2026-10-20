using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class ContagemDimensionalConfiguration : IEntityTypeConfiguration<ContagemDimensional>
{
  public void Configure(EntityTypeBuilder<ContagemDimensional> b)
  {
    b.ToTable("ContagemDimensional");
    b.HasKey(x => x.Id);
    // Espelha DECIMAL(18,4) do .sql. Sem isto o EF usa o default dele e trunca em silencio.
    b.Property(x => x.Conformes).HasPrecision(18, 4);
    b.Property(x => x.NaoConformes).HasPrecision(18, 4);
    // Mesmo motivo de MovimentacaoConfiguration: sem ValueGeneratedOnAdd() o EF sempre manda o valor
    // CLR e o DEFAULT do banco (DF_ContagemDimensional_DataHora) nunca dispara como rede de seguranca.
    b.Property(x => x.DataHora).ValueGeneratedOnAdd();
  }
}

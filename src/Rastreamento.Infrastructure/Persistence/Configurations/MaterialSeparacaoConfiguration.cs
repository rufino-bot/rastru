using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class MaterialSeparacaoConfiguration : IEntityTypeConfiguration<MaterialSeparacao>
{
  public void Configure(EntityTypeBuilder<MaterialSeparacao> b)
  {
    b.ToTable("MaterialSeparacao");
    b.HasKey(x => x.Id);
    b.Property(x => x.Tipo).HasMaxLength(20).IsRequired();
    // Espelha DECIMAL(18,4) do .sql. Sem isto o EF usa o default dele e trunca em silencio.
    b.Property(x => x.Quantidade).HasPrecision(18, 4);
    // Mesmo motivo de MovimentacaoConfiguration: sem ValueGeneratedOnAdd() o EF sempre manda o valor
    // CLR e o DEFAULT do banco (DF_MaterialSeparacao_DataHora) nunca dispara como rede de seguranca.
    b.Property(x => x.DataHora).ValueGeneratedOnAdd();
  }
}

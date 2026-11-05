using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class PerdaConfiguration : IEntityTypeConfiguration<Perda>
{
  public void Configure(EntityTypeBuilder<Perda> b)
  {
    b.ToTable("Perda");
    b.HasKey(x => x.Id);
    b.Property(x => x.Motivo).HasMaxLength(20).IsRequired();
    b.Property(x => x.Observacao).HasMaxLength(500);
    // Mesmo motivo de RemessaConfiguration: sem ValueGeneratedOnAdd() o EF sempre manda o valor CLR e o
    // DEFAULT do banco (DF_Perda_DataHora) nunca dispara como rede de seguranca. Em `Tudo` o default do
    // banco e 0, e o EF manda o valor da propriedade (o default do CLR, false, e o do banco, entao igual).
    b.Property(x => x.DataHora).ValueGeneratedOnAdd();
    b.Property(x => x.Tudo).ValueGeneratedNever();
  }
}

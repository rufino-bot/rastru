using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class EmbalagemConfiguration : IEntityTypeConfiguration<Embalagem>
{
  public void Configure(EntityTypeBuilder<Embalagem> b)
  {
    b.ToTable("Embalagem");
    b.HasKey(x => x.Id);
    b.Property(x => x.Tipo).HasMaxLength(10).IsRequired();
    // Mesmo motivo de MaterialSeparacaoConfiguration: sem ValueGeneratedOnAdd() o EF sempre manda o valor
    // CLR e o DEFAULT do banco (DF_Embalagem_DataHora) nunca dispara como rede de seguranca.
    b.Property(x => x.DataHora).ValueGeneratedOnAdd();
  }
}

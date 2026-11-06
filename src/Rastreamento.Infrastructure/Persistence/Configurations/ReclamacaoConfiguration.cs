using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class ReclamacaoConfiguration : IEntityTypeConfiguration<Reclamacao>
{
  public void Configure(EntityTypeBuilder<Reclamacao> b)
  {
    b.ToTable("Reclamacao");
    b.HasKey(x => x.Id);
    b.Property(x => x.Quantidade).HasPrecision(18, 4);
    b.Property(x => x.Descricao).HasMaxLength(500).IsRequired();
    // Mesmo motivo de MontagemConfiguration: sem ValueGeneratedOnAdd() o EF sempre manda o valor CLR e o
    // DEFAULT do banco (DF_Reclamacao_DataHora) nunca dispara como rede de seguranca.
    b.Property(x => x.DataHora).ValueGeneratedOnAdd();
  }
}

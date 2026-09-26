using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class MontagemConfiguration : IEntityTypeConfiguration<Montagem>
{
  public void Configure(EntityTypeBuilder<Montagem> b)
  {
    b.ToTable("Montagem");
    b.HasKey(x => x.Id);
    b.Property(x => x.Quantidade).HasPrecision(18, 4);
    // Mesmo gap de MovimentacaoConfiguration (e mesmo padrao de CriadoEm em
    // ArquivoDeComponenteConfiguration): sem ValueGeneratedOnAdd(), um caso de uso que esquecer
    // DataHora grava 0001-01-01 e DF_Montagem_DataHora nunca dispara.
    b.Property(x => x.DataHora).ValueGeneratedOnAdd();
  }
}

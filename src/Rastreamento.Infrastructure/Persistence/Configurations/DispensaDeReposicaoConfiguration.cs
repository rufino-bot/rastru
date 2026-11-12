using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class DispensaDeReposicaoConfiguration : IEntityTypeConfiguration<DispensaDeReposicao>
{
  public void Configure(EntityTypeBuilder<DispensaDeReposicao> b)
  {
    b.ToTable("DispensaDeReposicao");
    b.HasKey(x => x.Id);
    b.Property(x => x.Justificativa).HasMaxLength(500).IsRequired();
    // Mesmo motivo de MontagemConfiguration: o DEFAULT do banco (DF_DispensaDeReposicao_DataHora) como rede.
    b.Property(x => x.DataHora).ValueGeneratedOnAdd();
  }
}

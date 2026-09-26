using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class MovimentacaoConfiguration : IEntityTypeConfiguration<Movimentacao>
{
  public void Configure(EntityTypeBuilder<Movimentacao> b)
  {
    b.ToTable("Movimentacao");
    b.HasKey(x => x.Id);
    b.Property(x => x.Tipo).HasMaxLength(20).IsRequired();
    b.Property(x => x.OrigemPosicao).HasMaxLength(20).IsRequired();
    b.Property(x => x.DestinoPosicao).HasMaxLength(20).IsRequired();
    // Espelha DECIMAL(18,4) do .sql. Sem isto o EF usa o default dele e trunca em silencio.
    b.Property(x => x.Quantidade).HasPrecision(18, 4);
    // DataHora e gravado pelo caso de uso, mas ValueGeneratedOnAdd() e o que faz o DEFAULT do
    // banco (DF_Movimentacao_DataHora) funcionar como rede de seguranca de verdade: o EF so omite
    // a coluna do INSERT quando o valor CLR e o default de DateTime, deixando o DEFAULT disparar.
    // Sem isto o EF sempre manda o valor CLR — inclusive 0001-01-01, se o caso de uso esquecer de
    // preencher — e o DEFAULT nunca dispara. Mesmo padrao de CriadoEm em
    // ArquivoDeComponenteConfiguration.
    b.Property(x => x.DataHora).ValueGeneratedOnAdd();
  }
}

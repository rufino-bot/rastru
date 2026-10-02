using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class ImportacaoDeEstruturaFilhoConfiguration : IEntityTypeConfiguration<ImportacaoDeEstruturaFilho>
{
  public void Configure(EntityTypeBuilder<ImportacaoDeEstruturaFilho> b)
  {
    b.ToTable("ImportacaoDeEstruturaFilho");
    b.HasKey(f => f.Id);
    b.Property(f => f.QuantidadeLida).HasPrecision(18, 4);
    b.Property(f => f.Quantidade).HasPrecision(18, 4);

    // A segunda FK da tabela para o mesmo registro. A navegacao e opcional e so serve a quem monta
    // o rascunho com objetos ainda sem Id; ao ler, FilhoId basta.
    b.HasOne(f => f.Filho).WithMany().HasForeignKey(f => f.FilhoId)
        .OnDelete(DeleteBehavior.NoAction);
  }
}

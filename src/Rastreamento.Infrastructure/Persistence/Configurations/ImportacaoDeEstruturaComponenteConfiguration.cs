using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class ImportacaoDeEstruturaComponenteConfiguration : IEntityTypeConfiguration<ImportacaoDeEstruturaComponente>
{
  public void Configure(EntityTypeBuilder<ImportacaoDeEstruturaComponente> b)
  {
    b.ToTable("ImportacaoDeEstruturaComponente");
    b.HasKey(c => c.Id);
    b.Property(c => c.CodigoLido).HasMaxLength(50);
    b.Property(c => c.DescricaoLida).HasMaxLength(200).IsRequired();
    b.Property(c => c.CodigoNovo).HasMaxLength(50);
    b.Property(c => c.DescricaoNova).HasMaxLength(200);
    b.Property(c => c.TipoNovo).HasMaxLength(20);
    b.Property(c => c.EscolhaDeReceita).HasMaxLength(10);
    b.Property(c => c.ImpressaoDaReceitaDoCatalogo).HasColumnType("binary(32)");

    // Um codigo por rascunho, FILTRADO: um UNIQUE comum aceita um nulo so, e varias linhas sem part
    // number coexistem no mesmo rascunho. O indice vive no .sql; declarado aqui so para o modelo
    // espelhar o banco.
    b.HasIndex(c => new { c.ImportacaoId, c.CodigoLido })
        .IsUnique()
        .HasDatabaseName("UX_ImportacaoDeEstruturaComponente_Codigo")
        .HasFilter("[CodigoLido] IS NOT NULL");

    // As linhas em que este registro e o PAI. Sem cascata (decisao P6 do plano do import).
    b.HasMany(c => c.Filhos).WithOne().HasForeignKey(f => f.PaiId)
        .OnDelete(DeleteBehavior.NoAction);
  }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class ArquivoDeComponenteConfiguration : IEntityTypeConfiguration<ArquivoDeComponente>
{
  public void Configure(EntityTypeBuilder<ArquivoDeComponente> b)
  {
    b.ToTable("ArquivoDeComponente");
    b.HasKey(a => a.Id);
    b.Property(a => a.NomeOriginal).HasMaxLength(260).IsRequired();
    b.Property(a => a.Conteudo).IsRequired();
    // TamanhoEmBytes e Sha256 sao colunas CALCULADAS PERSISTED (emenda de 2026-09-12, ver XML doc
    // da entidade e §4.1 da spec de Fase 2B). ValueGeneratedOnAddOrUpdate() e o que faz o EF
    // excluir as duas do INSERT/UPDATE gerado e reler o valor calculado de volta apos salvar --
    // sem isto o EF tenta escrever nelas e TODO insert falha (SQL Server: Msg 271, "cannot be
    // modified because it is either a computed column..."). Medido nesta bancada: bastou
    // ValueGeneratedOnAddOrUpdate(), sem precisar de HasComputedColumnSql (que so importaria para
    // Migrations, e este projeto nao gera schema pelo EF).
    b.Property(a => a.TamanhoEmBytes).ValueGeneratedOnAddOrUpdate();
    b.Property(a => a.Sha256).HasColumnType("binary(32)").ValueGeneratedOnAddOrUpdate();
    // CriadoEm e gravado pelo DEFAULT do banco: sem isto o EF manda o default do DateTime.
    b.Property(a => a.CriadoEm).ValueGeneratedOnAdd();
    // Sem HasDefaultValue: Database First — o default vive so no .sql.
  }
}

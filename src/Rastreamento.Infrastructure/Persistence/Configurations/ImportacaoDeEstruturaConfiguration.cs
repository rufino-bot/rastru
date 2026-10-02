using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class ImportacaoDeEstruturaConfiguration : IEntityTypeConfiguration<ImportacaoDeEstrutura>
{
  public void Configure(EntityTypeBuilder<ImportacaoDeEstrutura> b)
  {
    b.ToTable("ImportacaoDeEstrutura");
    b.HasKey(i => i.Id);
    b.Property(i => i.NomeDoArquivo).HasMaxLength(260).IsRequired();
    b.Property(i => i.QuantidadeDaPeca).HasPrecision(18, 4);
    // Os dois vem do DEFAULT do banco na insercao (Database First); sem isto o EF mandaria o
    // default do DateTime. AtualizadoEm tambem e escrito de proposito em toda atualizacao
    // (ImportacaoDeEstruturaRepository.SalvarAsync), e o valor explicito vence o default.
    b.Property(i => i.CriadoEm).ValueGeneratedOnAdd();
    b.Property(i => i.AtualizadoEm).ValueGeneratedOnAdd();
    b.Property(i => i.Versao).IsRowVersion();

    // RaizId e uma coluna comum para o EF, sem navegacao nem FK no modelo: a FK existe no banco, e
    // e circular com ImportacaoDeEstruturaComponente. Modela-la aqui faria o EF recusar o ciclo
    // de insercao, e o repositorio grava o RaizId num segundo passo de qualquer jeito. A propriedade
    // Raiz da entidade serve so a quem monta o rascunho, e nao e mapeada.
    b.Ignore(i => i.Raiz);

    // Sem cascata, de proposito (decisao P6 do plano do import): ver o comentario do DDL.
    b.HasMany(i => i.Componentes).WithOne().HasForeignKey(c => c.ImportacaoId)
        .OnDelete(DeleteBehavior.NoAction);
  }
}

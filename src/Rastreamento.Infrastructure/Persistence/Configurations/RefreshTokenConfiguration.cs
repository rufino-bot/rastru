using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshToken");
        b.HasKey(t => t.Id);
        b.Property(t => t.TokenHash).HasMaxLength(200).IsRequired();
        b.Property(t => t.SubstituidoPorTokenHash).HasMaxLength(200);
        b.Property(t => t.CriadoEm).HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasOne(t => t.Usuario).WithMany().HasForeignKey(t => t.UsuarioId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class RastreamentoDbContext : DbContext
{
    public RastreamentoDbContext(DbContextOptions<RastreamentoDbContext> options) : base(options) { }

    public DbSet<Perfil> Perfis => Set<Perfil>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Setor> Setores => Set<Setor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RastreamentoDbContext).Assembly);
    }
}

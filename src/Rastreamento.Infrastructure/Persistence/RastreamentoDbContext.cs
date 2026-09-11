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
  public DbSet<Material> Materiais => Set<Material>();
  public DbSet<Componente> Componentes => Set<Componente>();
  public DbSet<ComponenteFilhoPadrao> FilhosPadrao => Set<ComponenteFilhoPadrao>();
  public DbSet<ComponenteMaterialPadrao> MateriaisPadrao => Set<ComponenteMaterialPadrao>();
  public DbSet<ComponenteRoteiroPadrao> RoteirosPadrao => Set<ComponenteRoteiroPadrao>();
  public DbSet<Pedido> Pedidos => Set<Pedido>();
  public DbSet<Agrupamento> Agrupamentos => Set<Agrupamento>();
  public DbSet<EstruturaItem> Estruturas => Set<EstruturaItem>();
  public DbSet<EstruturaMaterial> EstruturaMateriais => Set<EstruturaMaterial>();
  public DbSet<EstruturaRoteiro> EstruturaRoteiros => Set<EstruturaRoteiro>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(RastreamentoDbContext).Assembly);
  }
}

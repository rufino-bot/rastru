using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class UsuarioRepository : IUsuarioRepository
{
    private readonly RastreamentoDbContext _db;

    public UsuarioRepository(RastreamentoDbContext db) => _db = db;

    // Perfil vem junto porque o nome do perfil vira a claim `role` do access token.
    // Sem AsNoTracking de proposito: ver o contrato da interface (lockout depende do tracking).
    public Task<Usuario?> ObterPorNomeUsuarioAsync(string nomeUsuario, CancellationToken ct) =>
        _db.Usuarios.Include(u => u.Perfil)
            .SingleOrDefaultAsync(u => u.NomeUsuario == nomeUsuario, ct);

    public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}

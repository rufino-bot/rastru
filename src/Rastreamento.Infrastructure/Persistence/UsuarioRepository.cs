using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class UsuarioRepository : IUsuarioRepository
{
    private readonly RastreamentoDbContext _db;

    public UsuarioRepository(RastreamentoDbContext db) => _db = db;

    // Perfil vem junto porque o nome do perfil vira a claim `role` do access token.
    public Task<Usuario?> ObterPorNomeUsuarioAsync(string nomeUsuario, CancellationToken ct) =>
        _db.Usuarios.Include(u => u.Perfil)
            .SingleOrDefaultAsync(u => u.NomeUsuario == nomeUsuario, ct);

    public Task<Usuario?> ObterPorIdAsync(int id, CancellationToken ct) =>
        _db.Usuarios.Include(u => u.Perfil)
            .SingleOrDefaultAsync(u => u.Id == id, ct);
}

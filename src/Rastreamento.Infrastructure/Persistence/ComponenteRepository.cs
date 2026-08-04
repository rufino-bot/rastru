using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class ComponenteRepository : IComponenteRepository
{
  private readonly RastreamentoDbContext _db;

  public ComponenteRepository(RastreamentoDbContext db) => _db = db;

  // Sem AsNoTracking de proposito nos dois Obter*: ver o contrato da interface (Editar e
  // DefinirAtivo mutam a entidade devolvida e dependem do change tracking).
  public Task<Componente?> ObterPorIdAsync(int id, CancellationToken ct) =>
      _db.Componentes.SingleOrDefaultAsync(c => c.Id == id, ct);

  public Task<Componente?> ObterPorCodigoAsync(string codigo, CancellationToken ct) =>
      _db.Componentes.SingleOrDefaultAsync(c => c.Codigo == codigo, ct);

  public async Task<(IReadOnlyList<Componente> Itens, int Total)> ListarAsync(
      FiltroDeComponente filtro, CancellationToken ct)
  {
    var consulta = _db.Componentes.AsNoTracking()
        .Where(c => filtro.IncluirInativos || c.Ativo);

    if (!string.IsNullOrWhiteSpace(filtro.Busca))
    {
      // Sem ToLower(): a comparacao ja e case-insensitive pela collation da coluna, e ToLower()
      // na consulta impediria o uso de indice. Trim pelo mesmo motivo que o resto do projeto:
      // " CH " e " CH" tem que buscar a mesma coisa.
      var busca = filtro.Busca.Trim();
      consulta = consulta.Where(c => c.Codigo.Contains(busca) || c.Descricao.Contains(busca));
    }

    // Contado ANTES do Skip/Take e com o MESMO filtro: e o numero de paginas que o front usa.
    var total = await consulta.CountAsync(ct);

    // OrderBy obrigatorio e por Codigo de proposito: UQ_Componente_Codigo garante ordem TOTAL.
    // Sem ordem total, Skip/Take repete e pula linhas entre paginas.
    var itens = await consulta
        .OrderBy(c => c.Codigo)
        .Skip((filtro.Pagina - 1) * filtro.Tamanho)
        .Take(filtro.Tamanho)
        .ToListAsync(ct);

    return (itens, total);
  }

  public async Task AdicionarAsync(Componente componente, CancellationToken ct) =>
      await _db.Componentes.AddAsync(componente, ct);

  public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}

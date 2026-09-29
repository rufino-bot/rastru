using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class PedidoRepository : IPedidoRepository
{
  private readonly RastreamentoDbContext _db;

  public PedidoRepository(RastreamentoDbContext db) => _db = db;

  public Task<Pedido?> ObterPorIdAsync(int id, CancellationToken ct) =>
      _db.Pedidos.SingleOrDefaultAsync(p => p.Id == id, ct);

  public Task<Pedido?> ObterPorNumeroAsync(string numero, CancellationToken ct) =>
      _db.Pedidos.SingleOrDefaultAsync(p => p.Numero == numero, ct);

  public async Task<(IReadOnlyList<Pedido> Itens, int Total)> ListarAsync(
      FiltroDePedidos filtro, CancellationToken ct)
  {
    var consulta = _db.Pedidos.AsNoTracking();

    if (!string.IsNullOrEmpty(filtro.Busca))
    {
      // `Contains` (e nao `EF.Functions.Like`): o texto do usuario e literal, e "50%" ou "_" nao
      // podem virar curinga. Sem ToLower(): a collation da coluna ja ignora caixa.
      var busca = filtro.Busca;
      consulta = consulta.Where(p =>
          p.Numero.Contains(busca)
          || p.Cliente.Contains(busca)
          // Codigo do Componente de QUALQUER no do Pedido — a Peca ou um Item.
          || (from a in _db.Agrupamentos
              join n in _db.Estruturas on a.Id equals n.AgrupamentoId
              join c in _db.Componentes on n.ComponenteId equals c.Id
              where a.PedidoId == p.Id && c.Codigo.Contains(busca)
              select n.Id).Any());
    }

    if (filtro.Status.Count > 0)
    {
      var status = filtro.Status;
      consulta = consulta.Where(p => status.Contains(p.Status));
    }

    if (filtro.Materiais.Count > 0)
    {
      // O Material do NO (`EstruturaMaterial`), nunca o do catalogo; OU entre os pedidos.
      var materiais = filtro.Materiais;
      consulta = consulta.Where(p =>
          (from a in _db.Agrupamentos
           join n in _db.Estruturas on a.Id equals n.AgrupamentoId
           join m in _db.EstruturaMateriais on n.Id equals m.EstruturaItemId
           where a.PedidoId == p.Id && materiais.Contains(m.MaterialId)
           select m.Id).Any());
    }

    // Contado ANTES do Skip/Take e com o MESMO filtro: e o numero de paginas que o front usa.
    var total = await consulta.CountAsync(ct);

    // `DataAbertura` sozinha nao e ordem total; o desempate por `Id` e o que impede Skip/Take de
    // repetir e pular linhas entre paginas.
    var itens = await consulta
        .OrderByDescending(p => p.DataAbertura)
        .ThenByDescending(p => p.Id)
        .Skip((filtro.Pagina - 1) * filtro.Tamanho)
        .Take(filtro.Tamanho)
        .ToListAsync(ct);

    return (itens, total);
  }

  public async Task<IReadOnlyDictionary<string, int>> ContarPorStatusAsync(CancellationToken ct) =>
      await _db.Pedidos.AsNoTracking()
          .GroupBy(p => p.Status)
          .Select(g => new { Status = g.Key, Quantidade = g.Count() })
          .ToDictionaryAsync(x => x.Status, x => x.Quantidade, ct);

  public async Task<IReadOnlyList<Pedido>> ListarMaisAntigosAsync(
      IReadOnlyCollection<string> foraDosStatus, int quantos, CancellationToken ct) =>
      await _db.Pedidos.AsNoTracking()
          .Where(p => !foraDosStatus.Contains(p.Status))
          .OrderBy(p => p.DataAbertura)
          .ThenBy(p => p.Id)
          .Take(quantos)
          .ToListAsync(ct);

  public async Task<IReadOnlyList<Material>> ListarMateriaisEmUsoAsync(CancellationToken ct) =>
      await _db.Materiais.AsNoTracking()
          .Where(m => _db.EstruturaMateriais.Any(e => e.MaterialId == m.Id))
          .OrderBy(m => m.Descricao)
          .ThenBy(m => m.Codigo)
          .ToListAsync(ct);

  public Task<IReadOnlyDictionary<int, PausaAberta>> ListarPausasAbertasAsync(
      IReadOnlyCollection<int> pedidoIds, CancellationToken ct) =>
      PausasAbertas.ListarAsync(_db, pedidoIds, ct);

  public async Task AdicionarAsync(Pedido pedido, CancellationToken ct) =>
      await _db.Pedidos.AddAsync(pedido, ct);

  public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}

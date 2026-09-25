using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Pedido + Agrupamento + Componente reais, com nos, Setores e Roteiro criados sob demanda, e a
/// limpeza na ordem das FKs. Existe porque as tasks da Fase 3 criam a mesma arvore em varias classes
/// de teste de banco; cada teste cria a SUA (prefixo unico), e toda asercao fica escopada nos Ids
/// dela — nunca contagem global (licao do flaky de 2026-08-22, CLAUDE.md).
/// </summary>
internal sealed class ArvoreDeTesteNoBanco
{
  private readonly string _prefixo;

  public int AutorId { get; private init; }
  public int PedidoId { get; private init; }
  public int AgrupamentoId { get; private init; }
  public int ComponenteId { get; private init; }
  public List<int> SetorIds { get; } = [];

  private ArvoreDeTesteNoBanco(string prefixo) => _prefixo = prefixo;

  public static async Task<ArvoreDeTesteNoBanco> CriarAsync(RastreamentoDbContext db, string rotulo)
  {
    var prefixo = $"{rotulo}-{Guid.NewGuid():N}"[..14];
    var autor = (await db.Usuarios.AsNoTracking().SingleAsync(u => u.NomeUsuario == "admin")).Id;

    var pedido = new Pedido
    {
      Numero = prefixo, Cliente = "Cliente de teste", Tipo = "Fabricacao", Status = "Aberto",
      DataAbertura = DateTime.UtcNow, CriadoPorUsuarioId = autor,
    };
    db.Pedidos.Add(pedido);
    await db.SaveChangesAsync();

    var agrupamento = new Agrupamento
    {
      PedidoId = pedido.Id, Codigo = "AG-01", Tipo = "Avulso", CriadoPorUsuarioId = autor, CriadoEm = DateTime.UtcNow,
    };
    db.Agrupamentos.Add(agrupamento);

    var componente = new Componente
    {
      Codigo = prefixo, Descricao = "Componente de teste da Fase 3", Tipo = "Fabricado", Ativo = true,
    };
    db.Componentes.Add(componente);
    await db.SaveChangesAsync();

    return new ArvoreDeTesteNoBanco(prefixo)
    {
      AutorId = autor, PedidoId = pedido.Id, AgrupamentoId = agrupamento.Id, ComponenteId = componente.Id,
    };
  }

  public async Task<int> NovoSetorAsync(RastreamentoDbContext db, bool ativo = true)
  {
    var setor = new Setor { Nome = $"{_prefixo}-S{SetorIds.Count + 1}", Ativo = ativo };
    db.Setores.Add(setor);
    await db.SaveChangesAsync();
    SetorIds.Add(setor.Id);
    return setor.Id;
  }

  public async Task<int> NovaPecaAsync(RastreamentoDbContext db, decimal quantidade)
  {
    var peca = new EstruturaItem
    {
      AgrupamentoId = AgrupamentoId, ComponenteId = ComponenteId, NivelHierarquico = "Peca", Quantidade = quantidade,
    };
    db.Estruturas.Add(peca);
    await db.SaveChangesAsync();
    return peca.Id;
  }

  public async Task<int> NovoItemAsync(RastreamentoDbContext db, int paiId, decimal quantidade, decimal quantidadePorPai)
  {
    var item = new EstruturaItem
    {
      AgrupamentoId = AgrupamentoId, Descricao = $"Item de {paiId}", EstruturaPaiId = paiId,
      NivelHierarquico = "Item", Quantidade = quantidade, QuantidadePorPai = quantidadePorPai,
    };
    db.Estruturas.Add(item);
    await db.SaveChangesAsync();
    return item.Id;
  }

  /// <summary>Roteiro do no: um passo por Setor, `Ordem` 1, 2, 3... na ordem dada.</summary>
  public async Task RoteiroAsync(RastreamentoDbContext db, int estruturaItemId, params int[] setorIds)
  {
    for (var i = 0; i < setorIds.Length; i++)
      db.EstruturaRoteiros.Add(new EstruturaRoteiro { EstruturaItemId = estruturaItemId, SetorId = setorIds[i], Ordem = i + 1 });
    await db.SaveChangesAsync();
  }

  /// <summary>
  /// Contexto PROPRIO, para a limpeza nao herdar entidade rastreada nem transacao do teste. Uma unica
  /// instrucao `DELETE` por tabela resolve a FK autorreferenciada de `EstruturaItem` (o SQL Server
  /// confere a FK no fim da instrucao) — mesmo criterio de `EstruturaEndpointsTests.DisposeAsync`.
  /// </summary>
  public async Task LimparAsync(Func<RastreamentoDbContext> novoContexto)
  {
    await using var db = novoContexto();
    var ag = AgrupamentoId;
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM dbo.Movimentacao WHERE EstornoDeId IS NOT NULL AND EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM dbo.Movimentacao WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM dbo.Montagem WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM dbo.EstruturaMaterial WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM dbo.EstruturaRoteiro WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag}");
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Agrupamento WHERE Id = {ag}");
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Pedido WHERE Id = {PedidoId}");
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Componente WHERE Id = {ComponenteId}");
    foreach (var setorId in SetorIds)
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Setor WHERE Id = {setorId}");
  }
}

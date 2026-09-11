using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>Requer o SQL Server no ar (docker compose up -d) com o schema aplicado (Fase 2, Task 1:
/// CK_EstruturaItem_PecaTemComponente).</summary>
/// <remarks>
/// Entra na <see cref="ColecaoQueEscreveEmComponente"/> porque insere as proprias linhas em
/// <c>dbo.Componente</c> — o catalogo de demonstracao (<c>db/seed-demo.sql</c>) e OPCIONAL, entao
/// nao ha Componente algum garantido no banco para reusar. Nenhuma assercao aqui e sobre contagem
/// global: cada teste confere so os proprios ids/prefixos, como o resto da suite exige.
/// </remarks>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class EstruturaItemMapeamentoTests : TesteComBanco
{
  private static string NovoPrefixo() => $"est-{Guid.NewGuid():N}"[..12];

  /// <summary>Abre Pedido + Agrupamento reais: as FKs de EstruturaItem nao aceitam id inventado.</summary>
  private static async Task<(int PedidoId, int AgrupamentoId)> NovoAgrupamentoAsync(RastreamentoDbContext db)
  {
    var autor = (await db.Usuarios.AsNoTracking().SingleAsync(u => u.NomeUsuario == "admin")).Id;
    var pedido = new Pedido
    {
      Numero = $"est-{Guid.NewGuid():N}"[..25],
      Cliente = "Cliente de teste",
      Tipo = "Fabricacao",
      Status = "Aberto",
      DataAbertura = DateTime.UtcNow,
      CriadoPorUsuarioId = autor,
    };
    db.Pedidos.Add(pedido);
    await db.SaveChangesAsync();

    var agrupamento = new Agrupamento
    {
      PedidoId = pedido.Id,
      Codigo = "AG-01",
      Tipo = "Kit",
      CriadoPorUsuarioId = autor,
      CriadoEm = DateTime.UtcNow,
    };
    db.Agrupamentos.Add(agrupamento);
    await db.SaveChangesAsync();

    return (pedido.Id, agrupamento.Id);
  }

  private static async Task<int> NovoComponenteAsync(RastreamentoDbContext db, string prefixo)
  {
    var componente = new Componente
    {
      Codigo = $"{prefixo}-c",
      Descricao = "Componente de teste da estrutura",
      Tipo = "Fabricado",
      Ativo = true,
    };
    db.Componentes.Add(componente);
    await db.SaveChangesAsync();
    return componente.Id;
  }

  private static async Task<int> NovoSetorAsync(RastreamentoDbContext db, string prefixo)
  {
    var setor = new Setor { Nome = $"{prefixo}-s", Ativo = true };
    db.Setores.Add(setor);
    await db.SaveChangesAsync();
    return setor.Id;
  }

  private static async Task<int> NovoMaterialAsync(RastreamentoDbContext db, string prefixo)
  {
    var material = new Material
    {
      Codigo = $"{prefixo}-m",
      Descricao = "Material de teste da estrutura",
      UnidadeMedida = "KG",
      Ativo = true,
    };
    db.Materiais.Add(material);
    await db.SaveChangesAsync();
    return material.Id;
  }

  /// <summary>
  /// Limpeza na ordem que as FKs exigem: EstruturaRoteiro/EstruturaMaterial (filhos) antes de
  /// EstruturaItem, EstruturaItem antes de Componente/Setor, Agrupamento antes de Pedido.
  /// </summary>
  private static async Task LimparAsync(int pedidoId, int agrupamentoId)
  {
    await using var db = NovoContexto();

    var idsDaEstrutura = await db.Estruturas.AsNoTracking()
        .Where(e => e.AgrupamentoId == agrupamentoId)
        .Select(e => e.Id)
        .ToListAsync();

    db.EstruturaRoteiros.RemoveRange(
        await db.EstruturaRoteiros.Where(r => idsDaEstrutura.Contains(r.EstruturaItemId)).ToListAsync());
    db.EstruturaMateriais.RemoveRange(
        await db.EstruturaMateriais.Where(m => idsDaEstrutura.Contains(m.EstruturaItemId)).ToListAsync());
    await db.SaveChangesAsync();

    // Filhos (EstruturaPaiId != null) antes dos pais: EstruturaPaiId e escalar, sem HasOne/WithMany
    // configurado (nao ha necessidade de navegacao para os testes de mapeamento), entao o EF nao
    // conhece FK_EstruturaItem_Pai como relacionamento e nao ordena um RemoveRange unico sozinho.
    // Duas passadas explicitas evitam depender dessa ordem.
    db.Estruturas.RemoveRange(await db.Estruturas
        .Where(e => e.AgrupamentoId == agrupamentoId && e.EstruturaPaiId != null).ToListAsync());
    await db.SaveChangesAsync();

    db.Estruturas.RemoveRange(await db.Estruturas.Where(e => e.AgrupamentoId == agrupamentoId).ToListAsync());
    await db.SaveChangesAsync();

    db.Agrupamentos.RemoveRange(await db.Agrupamentos.Where(a => a.Id == agrupamentoId).ToListAsync());
    await db.SaveChangesAsync();

    db.Pedidos.RemoveRange(await db.Pedidos.Where(p => p.Id == pedidoId).ToListAsync());
    await db.SaveChangesAsync();
  }

  private static async Task LimparComponenteAsync(int componenteId)
  {
    await using var db = NovoContexto();
    db.Componentes.RemoveRange(await db.Componentes.Where(c => c.Id == componenteId).ToListAsync());
    await db.SaveChangesAsync();
  }

  private static async Task LimparSetorAsync(int setorId)
  {
    await using var db = NovoContexto();
    db.Setores.RemoveRange(await db.Setores.Where(s => s.Id == setorId).ToListAsync());
    await db.SaveChangesAsync();
  }

  private static async Task LimparMaterialAsync(int materialId)
  {
    await using var db = NovoContexto();
    db.Materiais.RemoveRange(await db.Materiais.Where(m => m.Id == materialId).ToListAsync());
    await db.SaveChangesAsync();
  }

  [Fact]
  public async Task Peca_e_Item_gravam_e_leem_com_a_autorreferencia()
  {
    var prefixo = NovoPrefixo();
    await using var db = NovoContexto();
    var (pedidoId, agrupamentoId) = await NovoAgrupamentoAsync(db);
    var componenteId = await NovoComponenteAsync(db, prefixo);

    try
    {
      var peca = new EstruturaItem
      {
        AgrupamentoId = agrupamentoId,
        ComponenteId = componenteId,
        EstruturaPaiId = null,
        NivelHierarquico = "Peca",
        Quantidade = 10,
      };
      db.Estruturas.Add(peca);
      await db.SaveChangesAsync();

      var item = new EstruturaItem
      {
        AgrupamentoId = agrupamentoId,
        ComponenteId = componenteId,
        EstruturaPaiId = peca.Id,
        NivelHierarquico = "Item",
        Quantidade = 40,
      };
      db.Estruturas.Add(item);
      await db.SaveChangesAsync();

      await using var dbLeitura = NovoContexto();
      var itemLido = await dbLeitura.Estruturas.AsNoTracking().SingleAsync(e => e.Id == item.Id);

      Assert.Equal(peca.Id, itemLido.EstruturaPaiId);
      Assert.Equal("Item", itemLido.NivelHierarquico);
      Assert.Equal(40, itemLido.Quantidade);
    }
    finally
    {
      await LimparAsync(pedidoId, agrupamentoId);
      await LimparComponenteAsync(componenteId);
    }
  }

  [Fact]
  public async Task Peca_sem_Componente_e_recusada_pelo_banco()
  {
    await using var db = NovoContexto();
    var (pedidoId, agrupamentoId) = await NovoAgrupamentoAsync(db);

    try
    {
      var peca = new EstruturaItem
      {
        AgrupamentoId = agrupamentoId,
        ComponenteId = null,
        EstruturaPaiId = null,
        NivelHierarquico = "Peca",
        Quantidade = 1,
      };
      db.Estruturas.Add(peca);

      // CK_EstruturaItem_PecaTemComponente (Task 1, Fase 2): uma Peca (no sem pai) sempre exige
      // Componente. So um Item ad-hoc pode ficar sem ele.
      var excecao = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

      // Prende o teste a ESTA constraint por construcao: sem conferir o nome, uma FK violada por
      // acidente no futuro faria o teste passar provando outra coisa.
      var mensagem = excecao.InnerException?.Message ?? excecao.Message;
      Assert.Contains("CK_EstruturaItem_PecaTemComponente", mensagem);
    }
    finally
    {
      await LimparAsync(pedidoId, agrupamentoId);
    }
  }

  [Fact]
  public async Task Item_sem_Componente_e_aceito()
  {
    var prefixo = NovoPrefixo();
    await using var db = NovoContexto();
    var (pedidoId, agrupamentoId) = await NovoAgrupamentoAsync(db);
    var componenteId = await NovoComponenteAsync(db, prefixo);

    try
    {
      var peca = new EstruturaItem
      {
        AgrupamentoId = agrupamentoId,
        ComponenteId = componenteId,
        EstruturaPaiId = null,
        NivelHierarquico = "Peca",
        Quantidade = 10,
      };
      db.Estruturas.Add(peca);
      await db.SaveChangesAsync();

      var itemAdHoc = new EstruturaItem
      {
        AgrupamentoId = agrupamentoId,
        ComponenteId = null,
        EstruturaPaiId = peca.Id,
        NivelHierarquico = "Item",
        Quantidade = 5,
        Descricao = "Item ad-hoc, sem base no catalogo",
      };
      db.Estruturas.Add(itemAdHoc);
      // Nao deve lancar: CK_EstruturaItem_PecaTemComponente so exige Componente quando
      // NivelHierarquico = 'Peca'.
      await db.SaveChangesAsync();

      await using var dbLeitura = NovoContexto();
      var itemLido = await dbLeitura.Estruturas.AsNoTracking().SingleAsync(e => e.Id == itemAdHoc.Id);

      Assert.Null(itemLido.ComponenteId);
      Assert.Equal("Item ad-hoc, sem base no catalogo", itemLido.Descricao);
    }
    finally
    {
      await LimparAsync(pedidoId, agrupamentoId);
      await LimparComponenteAsync(componenteId);
    }
  }

  [Fact]
  public async Task Roteiro_preserva_ordem_com_setor_repetido()
  {
    var prefixo = NovoPrefixo();
    await using var db = NovoContexto();
    var (pedidoId, agrupamentoId) = await NovoAgrupamentoAsync(db);
    var componenteId = await NovoComponenteAsync(db, prefixo);
    var setorId = await NovoSetorAsync(db, prefixo);

    try
    {
      var peca = new EstruturaItem
      {
        AgrupamentoId = agrupamentoId,
        ComponenteId = componenteId,
        EstruturaPaiId = null,
        NivelHierarquico = "Peca",
        Quantidade = 1,
      };
      db.Estruturas.Add(peca);
      await db.SaveChangesAsync();

      // Regra 21: setor repetido no roteiro e RETORNO ao mesmo setor, nao duplicata. A unicidade
      // do schema (EstruturaItemId, Ordem) permite as duas linhas porque a Ordem difere.
      db.EstruturaRoteiros.AddRange(
          new EstruturaRoteiro { EstruturaItemId = peca.Id, SetorId = setorId, Ordem = 1 },
          new EstruturaRoteiro { EstruturaItemId = peca.Id, SetorId = setorId, Ordem = 3 });
      await db.SaveChangesAsync();

      await using var dbLeitura = NovoContexto();
      var roteiro = await dbLeitura.EstruturaRoteiros.AsNoTracking()
          .Where(r => r.EstruturaItemId == peca.Id)
          .OrderBy(r => r.Ordem)
          .ToListAsync();

      Assert.Equal(2, roteiro.Count);
      Assert.Equal([1, 3], roteiro.Select(r => r.Ordem).ToArray());
      Assert.All(roteiro, r => Assert.Equal(setorId, r.SetorId));
    }
    finally
    {
      await LimparAsync(pedidoId, agrupamentoId);
      await LimparComponenteAsync(componenteId);
      await LimparSetorAsync(setorId);
    }
  }

  /// <summary>
  /// EstruturaItem.Quantidade e EstruturaMaterial.Quantidade sao DECIMAL(18,4) no .sql; sem
  /// <c>HasPrecision(18, 4)</c> nas duas configurations o EF assume o default dele e trunca em
  /// silencio. 0.0001 e o menor valor representavel — se voltar 0, o mapeamento esta errado.
  /// Este e tambem o primeiro teste que grava uma linha de EstruturaMaterial: antes desta task
  /// nenhum teste cobria essa entidade.
  /// </summary>
  [Fact]
  public async Task Quantidade_preserva_as_quatro_casas_decimais_em_EstruturaItem_e_EstruturaMaterial()
  {
    var prefixo = NovoPrefixo();
    await using var db = NovoContexto();
    var (pedidoId, agrupamentoId) = await NovoAgrupamentoAsync(db);
    var componenteId = await NovoComponenteAsync(db, prefixo);
    var materialId = await NovoMaterialAsync(db, prefixo);

    try
    {
      var peca = new EstruturaItem
      {
        AgrupamentoId = agrupamentoId,
        ComponenteId = componenteId,
        EstruturaPaiId = null,
        NivelHierarquico = "Peca",
        Quantidade = 0.0001m,
      };
      db.Estruturas.Add(peca);
      await db.SaveChangesAsync();

      db.EstruturaMateriais.Add(new EstruturaMaterial
      {
        EstruturaItemId = peca.Id,
        MaterialId = materialId,
        Quantidade = 0.0001m,
      });
      await db.SaveChangesAsync();

      await using var dbLeitura = NovoContexto();
      var pecaLida = await dbLeitura.Estruturas.AsNoTracking().SingleAsync(e => e.Id == peca.Id);
      var materialLido = await dbLeitura.EstruturaMateriais.AsNoTracking()
          .SingleAsync(m => m.EstruturaItemId == peca.Id);

      Assert.Equal(0.0001m, pecaLida.Quantidade);
      Assert.Equal(0.0001m, materialLido.Quantidade);
    }
    finally
    {
      await LimparAsync(pedidoId, agrupamentoId);
      await LimparComponenteAsync(componenteId);
      await LimparMaterialAsync(materialId);
    }
  }
}

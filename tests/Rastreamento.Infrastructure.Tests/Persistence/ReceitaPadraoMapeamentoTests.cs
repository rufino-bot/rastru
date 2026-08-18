using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Mapeamento das 3 tabelas de receita padrao contra o SQL Server REAL. Banco em memoria nao
/// serviria: o que se prova aqui e que os nomes de coluna, a precisao do DECIMAL(18,4) e as FKs
/// batem com `specs/02-modelo-de-dados.sql` — a fonte de verdade do schema.
/// </summary>
public class ReceitaPadraoMapeamentoTests : FixtureDeReceitaPadrao
{
  /// <summary>
  /// Remove a linha de ComponenteFilhoPadrao antes dos dois Componentes que ela referencia — as
  /// FKs apontam para eles, entao a ordem inversa falha.
  /// </summary>
  private static async Task LimparFilhoPadraoAsync(int paiId, int filhoId)
  {
    await using var dbLimpeza = NovoContexto();
    dbLimpeza.FilhosPadrao.RemoveRange(
        await dbLimpeza.FilhosPadrao.Where(f => f.ComponentePaiId == paiId).ToListAsync());
    await dbLimpeza.SaveChangesAsync();

    dbLimpeza.Componentes.RemoveRange(
        await dbLimpeza.Componentes.Where(c => c.Id == paiId || c.Id == filhoId).ToListAsync());
    await dbLimpeza.SaveChangesAsync();
  }

  private static async Task LimparMaterialPadraoAsync(int componenteId, int materialId)
  {
    await using var dbLimpeza = NovoContexto();
    dbLimpeza.MateriaisPadrao.RemoveRange(
        await dbLimpeza.MateriaisPadrao.Where(m => m.ComponenteId == componenteId).ToListAsync());
    await dbLimpeza.SaveChangesAsync();

    dbLimpeza.Componentes.RemoveRange(
        await dbLimpeza.Componentes.Where(c => c.Id == componenteId).ToListAsync());
    dbLimpeza.Materiais.RemoveRange(
        await dbLimpeza.Materiais.Where(m => m.Id == materialId).ToListAsync());
    await dbLimpeza.SaveChangesAsync();
  }

  private static async Task LimparRoteiroPadraoAsync(int componenteId, int setorId)
  {
    await using var dbLimpeza = NovoContexto();
    dbLimpeza.RoteirosPadrao.RemoveRange(
        await dbLimpeza.RoteirosPadrao.Where(r => r.ComponenteId == componenteId).ToListAsync());
    await dbLimpeza.SaveChangesAsync();

    dbLimpeza.Componentes.RemoveRange(
        await dbLimpeza.Componentes.Where(c => c.Id == componenteId).ToListAsync());
    dbLimpeza.Setores.RemoveRange(
        await dbLimpeza.Setores.Where(s => s.Id == setorId).ToListAsync());
    await dbLimpeza.SaveChangesAsync();
  }

  [Fact]
  public async Task Filho_padrao_grava_e_le_com_a_quantidade_intacta()
  {
    await using var db = NovoContexto();
    var (pai, filho) = await DoisComponentes(db);

    try
    {
      db.FilhosPadrao.Add(new ComponenteFilhoPadrao
      {
        ComponentePaiId = pai.Id,
        ComponenteFilhoId = filho.Id,
        QuantidadePadrao = 2.5m,
      });
      await db.SaveChangesAsync();

      var lida = await db.FilhosPadrao.AsNoTracking()
          .SingleAsync(f => f.ComponentePaiId == pai.Id);

      Assert.Equal(filho.Id, lida.ComponenteFilhoId);
      Assert.Equal(2.5m, lida.QuantidadePadrao);
    }
    finally
    {
      await LimparFilhoPadraoAsync(pai.Id, filho.Id);
    }
  }

  /// <summary>
  /// A precisao NAO e detalhe: DECIMAL(18,4) tem 4 casas, e sem `HasPrecision(18, 4)` o EF assume
  /// o default dele e trunca em silencio. 0.0001 e o menor valor representavel — se voltar 0, o
  /// mapeamento esta errado.
  /// </summary>
  [Fact]
  public async Task Quantidade_preserva_as_quatro_casas_decimais()
  {
    await using var db = NovoContexto();
    var (pai, filho) = await DoisComponentes(db);

    try
    {
      db.FilhosPadrao.Add(new ComponenteFilhoPadrao
      {
        ComponentePaiId = pai.Id,
        ComponenteFilhoId = filho.Id,
        QuantidadePadrao = 0.0001m,
      });
      await db.SaveChangesAsync();

      var lida = await db.FilhosPadrao.AsNoTracking()
          .SingleAsync(f => f.ComponentePaiId == pai.Id);

      Assert.Equal(0.0001m, lida.QuantidadePadrao);
    }
    finally
    {
      await LimparFilhoPadraoAsync(pai.Id, filho.Id);
    }
  }

  [Fact]
  public async Task Material_padrao_grava_e_le()
  {
    await using var db = NovoContexto();
    var componente = await UmComponente(db);
    var material = await UmMaterial(db);

    try
    {
      db.MateriaisPadrao.Add(new ComponenteMaterialPadrao
      {
        ComponenteId = componente.Id,
        MaterialId = material.Id,
        QuantidadePadrao = 3m,
      });
      await db.SaveChangesAsync();

      var lida = await db.MateriaisPadrao.AsNoTracking()
          .SingleAsync(m => m.ComponenteId == componente.Id);

      Assert.Equal(material.Id, lida.MaterialId);
      Assert.Equal(3m, lida.QuantidadePadrao);
    }
    finally
    {
      await LimparMaterialPadraoAsync(componente.Id, material.Id);
    }
  }

  [Fact]
  public async Task Roteiro_padrao_grava_e_le_a_ordem()
  {
    await using var db = NovoContexto();
    var componente = await UmComponente(db);
    var setor = await UmSetor(db);

    try
    {
      db.RoteirosPadrao.Add(new ComponenteRoteiroPadrao
      {
        ComponenteId = componente.Id,
        SetorId = setor.Id,
        Ordem = 1,
      });
      await db.SaveChangesAsync();

      var lida = await db.RoteirosPadrao.AsNoTracking()
          .SingleAsync(r => r.ComponenteId == componente.Id);

      Assert.Equal(setor.Id, lida.SetorId);
      Assert.Equal(1, lida.Ordem);
    }
    finally
    {
      await LimparRoteiroPadraoAsync(componente.Id, setor.Id);
    }
  }

  /// <summary>
  /// O MESMO setor duas vezes no roteiro e PERMITIDO — o UQ e (ComponenteId, Ordem), NAO
  /// (ComponenteId, SetorId). Isso e deliberado no schema e significa RETORNO AO SETOR (a peca
  /// volta a usinagem depois da solda). Este teste existe para que ninguem "corrija" isso.
  /// </summary>
  [Fact]
  public async Task Mesmo_setor_pode_repetir_no_roteiro_em_ordens_diferentes()
  {
    await using var db = NovoContexto();
    var componente = await UmComponente(db);
    var setor = await UmSetor(db);

    try
    {
      db.RoteirosPadrao.AddRange(
          new ComponenteRoteiroPadrao { ComponenteId = componente.Id, SetorId = setor.Id, Ordem = 1 },
          new ComponenteRoteiroPadrao { ComponenteId = componente.Id, SetorId = setor.Id, Ordem = 2 });

      // Nao lanca: o banco aceita, e a aplicacao nao pode inventar restricao que o schema nao tem.
      await db.SaveChangesAsync();

      var linhas = await db.RoteirosPadrao.AsNoTracking()
          .Where(r => r.ComponenteId == componente.Id).ToListAsync();

      Assert.Equal(2, linhas.Count);
      Assert.All(linhas, l => Assert.Equal(setor.Id, l.SetorId));
    }
    finally
    {
      await LimparRoteiroPadraoAsync(componente.Id, setor.Id);
    }
  }
}

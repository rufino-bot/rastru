using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests;

/// <summary>
/// Mapeamento das 3 tabelas de receita padrao contra o SQL Server REAL. Banco em memoria nao
/// serviria: o que se prova aqui e que os nomes de coluna, a precisao do DECIMAL(18,4) e as FKs
/// batem com `specs/02-modelo-de-dados.sql` — a fonte de verdade do schema.
/// </summary>
public class ReceitaPadraoMapeamentoTests : TesteComBanco
{
  private static string NovoCodigo() => $"RP-{Guid.NewGuid():N}"[..12];

  private static async Task<Componente> UmComponente(RastreamentoDbContext db)
  {
    var componente = new Componente
    {
      Codigo = NovoCodigo(),
      Descricao = "Componente de teste",
      Tipo = "Fabricado",
      Ativo = true,
    };
    db.Componentes.Add(componente);
    await db.SaveChangesAsync();
    return componente;
  }

  private static async Task<(Componente pai, Componente filho)> DoisComponentes(RastreamentoDbContext db)
  {
    var pai = new Componente
    {
      Codigo = NovoCodigo(),
      Descricao = "Componente pai de teste",
      Tipo = "Montagem",
      Ativo = true,
    };
    var filho = new Componente
    {
      Codigo = NovoCodigo(),
      Descricao = "Componente filho de teste",
      Tipo = "Fabricado",
      Ativo = true,
    };
    db.Componentes.AddRange(pai, filho);
    await db.SaveChangesAsync();
    return (pai, filho);
  }

  private static async Task<Material> UmMaterial(RastreamentoDbContext db)
  {
    var material = new Material
    {
      Codigo = NovoCodigo(),
      Descricao = "Material de teste",
      UnidadeMedida = "KG",
      Ativo = true,
    };
    db.Materiais.Add(material);
    await db.SaveChangesAsync();
    return material;
  }

  private static async Task<Setor> UmSetor(RastreamentoDbContext db)
  {
    var setor = new Setor { Nome = $"setor-{Guid.NewGuid():N}", Ativo = true };
    db.Setores.Add(setor);
    await db.SaveChangesAsync();
    return setor;
  }

  [Fact]
  public async Task Filho_padrao_grava_e_le_com_a_quantidade_intacta()
  {
    await using var db = NovoContexto();
    var (pai, filho) = await DoisComponentes(db);

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

  [Fact]
  public async Task Material_padrao_grava_e_le()
  {
    await using var db = NovoContexto();
    var componente = await UmComponente(db);
    var material = await UmMaterial(db);

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

  [Fact]
  public async Task Roteiro_padrao_grava_e_le_a_ordem()
  {
    await using var db = NovoContexto();
    var componente = await UmComponente(db);
    var setor = await UmSetor(db);

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
}

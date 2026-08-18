using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// O repositorio da receita padrao contra o SQL Server REAL. O que se prova aqui e a SEMANTICA DE
/// SUBSTITUICAO — apagar as linhas antigas do componente e gravar as novas num unico
/// <c>SaveChanges</c> — e o escopo desse apagamento. Banco em memoria nao provaria: o ponto e o
/// comportamento transacional e as FKs de verdade.
/// </summary>
public class ReceitaPadraoRepositoryTests : FixtureDeReceitaPadrao
{
  /// <summary>
  /// Apaga, em ordem de FK, tudo o que um teste deste arquivo cria. As linhas de receita saem
  /// antes dos Componentes e Setores que elas referenciam — a ordem inversa viola a FK.
  ///
  /// Recebe conjuntos, e nao um par fixo, porque cada teste daqui cria uma quantidade diferente
  /// de componentes (pai + dois filhos, dois pais + um filho...). Sem isto cada execucao contra o
  /// banco de dev deixaria linha orfa acumulando sem limite.
  /// </summary>
  private static async Task LimparAsync(int[] componenteIds, params int[] setorIds)
  {
    await using var db = NovoContexto();

    db.FilhosPadrao.RemoveRange(await db.FilhosPadrao
        .Where(f => componenteIds.Contains(f.ComponentePaiId)
                 || componenteIds.Contains(f.ComponenteFilhoId))
        .ToListAsync());
    db.MateriaisPadrao.RemoveRange(await db.MateriaisPadrao
        .Where(m => componenteIds.Contains(m.ComponenteId)).ToListAsync());
    db.RoteirosPadrao.RemoveRange(await db.RoteirosPadrao
        .Where(r => componenteIds.Contains(r.ComponenteId)).ToListAsync());
    await db.SaveChangesAsync();

    db.Componentes.RemoveRange(
        await db.Componentes.Where(c => componenteIds.Contains(c.Id)).ToListAsync());
    db.Setores.RemoveRange(await db.Setores.Where(s => setorIds.Contains(s.Id)).ToListAsync());
    await db.SaveChangesAsync();
  }

  /// <summary>
  /// A substituicao e o coracao do contrato: POST significa "a receita passa a ser EXATAMENTE
  /// estas linhas". As antigas somem, as novas entram, num SaveChanges so.
  /// </summary>
  [Fact]
  public async Task Substituir_filhos_apaga_as_linhas_antigas_e_grava_as_novas()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var pai = await UmComponente(db);
    var filhoA = await UmComponente(db);
    var filhoB = await UmComponente(db);

    try
    {
      await repo.SubstituirFilhosAsync(pai.Id, [
        new ComponenteFilhoPadrao
        {
          ComponentePaiId = pai.Id, ComponenteFilhoId = filhoA.Id, QuantidadePadrao = 1m,
        },
      ], CancellationToken.None);

      await repo.SubstituirFilhosAsync(pai.Id, [
        new ComponenteFilhoPadrao
        {
          ComponentePaiId = pai.Id, ComponenteFilhoId = filhoB.Id, QuantidadePadrao = 7m,
        },
      ], CancellationToken.None);

      var linhas = await repo.ListarFilhosAsync(pai.Id, CancellationToken.None);

      var unica = Assert.Single(linhas);
      Assert.Equal(filhoB.Id, unica.ComponenteFilhoId);
      Assert.Equal(7m, unica.QuantidadePadrao);
    }
    finally
    {
      await LimparAsync([pai.Id, filhoA.Id, filhoB.Id]);
    }
  }

  /// <summary>Lista vazia APAGA — e o unico caminho de remocao que existe (nao ha DELETE).</summary>
  [Fact]
  public async Task Substituir_filhos_com_lista_vazia_apaga_tudo()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var pai = await UmComponente(db);
    var filho = await UmComponente(db);

    try
    {
      await repo.SubstituirFilhosAsync(pai.Id, [
        new ComponenteFilhoPadrao
        {
          ComponentePaiId = pai.Id, ComponenteFilhoId = filho.Id, QuantidadePadrao = 1m,
        },
      ], CancellationToken.None);

      await repo.SubstituirFilhosAsync(pai.Id, [], CancellationToken.None);

      Assert.Empty(await repo.ListarFilhosAsync(pai.Id, CancellationToken.None));
    }
    finally
    {
      await LimparAsync([pai.Id, filho.Id]);
    }
  }

  /// <summary>
  /// A substituicao e ESCOPADA ao componente: mexer na receita de A nao pode tocar na de B.
  /// Sem o `Where(ComponentePaiId == id)` no delete, este teste morre.
  /// </summary>
  [Fact]
  public async Task Substituir_filhos_nao_toca_na_receita_de_outro_componente()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var paiA = await UmComponente(db);
    var paiB = await UmComponente(db);
    var filho = await UmComponente(db);

    try
    {
      await repo.SubstituirFilhosAsync(paiB.Id, [
        new ComponenteFilhoPadrao
        {
          ComponentePaiId = paiB.Id, ComponenteFilhoId = filho.Id, QuantidadePadrao = 4m,
        },
      ], CancellationToken.None);

      await repo.SubstituirFilhosAsync(paiA.Id, [], CancellationToken.None);

      Assert.Single(await repo.ListarFilhosAsync(paiB.Id, CancellationToken.None));
    }
    finally
    {
      await LimparAsync([paiA.Id, paiB.Id, filho.Id]);
    }
  }

  /// <summary>O roteiro sai ORDENADO por Ordem — a tela depende disso para desenhar a sequencia.</summary>
  [Fact]
  public async Task Listar_roteiro_devolve_ordenado_por_ordem()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var componente = await UmComponente(db);
    var setorA = await UmSetor(db);
    var setorB = await UmSetor(db);

    try
    {
      // Inseridos FORA de ordem de proposito: se o repositorio nao ordenar, o teste pega.
      await repo.SubstituirRoteiroAsync(componente.Id, [
        new ComponenteRoteiroPadrao { ComponenteId = componente.Id, SetorId = setorB.Id, Ordem = 2 },
        new ComponenteRoteiroPadrao { ComponenteId = componente.Id, SetorId = setorA.Id, Ordem = 1 },
      ], CancellationToken.None);

      var linhas = await repo.ListarRoteiroAsync(componente.Id, CancellationToken.None);

      Assert.Equal([setorA.Id, setorB.Id], linhas.Select(l => l.SetorId));
    }
    finally
    {
      await LimparAsync([componente.Id], setorA.Id, setorB.Id);
    }
  }

  /// <summary>
  /// A deteccao de ciclo (Task 5) precisa do grafo INTEIRO, nao so das linhas de um componente.
  /// </summary>
  [Fact]
  public async Task Listar_todas_as_arestas_traz_linhas_de_componentes_diferentes()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var paiA = await UmComponente(db);
    var paiB = await UmComponente(db);
    var filho = await UmComponente(db);

    try
    {
      await repo.SubstituirFilhosAsync(paiA.Id, [
        new ComponenteFilhoPadrao
        {
          ComponentePaiId = paiA.Id, ComponenteFilhoId = filho.Id, QuantidadePadrao = 1m,
        },
      ], CancellationToken.None);
      await repo.SubstituirFilhosAsync(paiB.Id, [
        new ComponenteFilhoPadrao
        {
          ComponentePaiId = paiB.Id, ComponenteFilhoId = filho.Id, QuantidadePadrao = 1m,
        },
      ], CancellationToken.None);

      var arestas = await repo.ListarTodasAsArestasAsync(CancellationToken.None);

      Assert.Contains(arestas, a => a.ComponentePaiId == paiA.Id);
      Assert.Contains(arestas, a => a.ComponentePaiId == paiB.Id);
    }
    finally
    {
      await LimparAsync([paiA.Id, paiB.Id, filho.Id]);
    }
  }
}

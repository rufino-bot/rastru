using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Cobre <see cref="EstruturaRepository"/> (Fase 2, Task 3) contra o SQL Server real: a
/// atomicidade de `GravarArvoreAsync` e a leitura de `LerReceitaCompletaAsync`.
///
/// Nao esta no brief da Task 3 (que so pede testes de Application.Tests, com fake). Existe porque
/// o proprio brief manda medir a mutacao "remover a transacao e gravar no a no -&gt; tem de quebrar
/// num caso com erro no meio da arvore" — e essa mutacao nao tem ONDE quebrar sem um teste contra
/// o banco real: o fake de `Application.Tests` (`FakeEstruturaRepo`) nao usa transacao nenhuma, e
/// sem este arquivo a suite inteira ficaria verde com a transacao removida. Ver o relatorio da
/// Task 3.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class EstruturaRepositoryTests : TesteComBanco
{
  private static string NovoPrefixo() => $"er-{Guid.NewGuid():N}"[..12];

  /// <summary>Abre Pedido + Agrupamento reais: as FKs de EstruturaItem nao aceitam id inventado.</summary>
  private static async Task<(int PedidoId, int AgrupamentoId)> NovoAgrupamentoAsync(RastreamentoDbContext db)
  {
    var autor = (await db.Usuarios.AsNoTracking().SingleAsync(u => u.NomeUsuario == "admin")).Id;
    var pedido = new Pedido
    {
      Numero = $"er-{Guid.NewGuid():N}"[..25],
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

  private static async Task<int> NovoComponenteAsync(RastreamentoDbContext db, string prefixo, string sufixo)
  {
    var componente = new Componente
    {
      Codigo = $"{prefixo}-{sufixo}",
      Descricao = "Componente de teste do EstruturaRepository",
      Tipo = "Fabricado",
      Ativo = true,
    };
    db.Componentes.Add(componente);
    await db.SaveChangesAsync();
    return componente.Id;
  }

  /// <summary>
  /// Mesma ordem de limpeza de `EstruturaItemMapeamentoTests`: filhos antes de pais, FKs primeiro.
  ///
  /// M3 da review da Task 4: os nao-raizes saem NIVEL A NIVEL, do mais profundo para o mais raso —
  /// nao mais num `RemoveRange` so. Um unico `RemoveRange` sobre TODOS os nao-raizes funciona numa
  /// arvore de 2 niveis (raiz+filho, o unico formato que os testes deste arquivo tinham antes da
  /// Task 4), mas numa arvore de 3+ niveis com dois nos nao-raiz numa relacao pai-filho entre si
  /// (o EF nao ordena o DELETE de duas linhas da mesma tabela sem tracking de navegacao entre elas)
  /// pode tentar apagar o no do MEIO antes do neto que ainda aponta pra ele
  /// (`FK_EstruturaItem_Pai`) — exatamente quando este `finally` roda por o teste ter FALHADO antes
  /// de a arvore ja ter sido reduzida, mascarando a asercao original com uma `DbUpdateException` de
  /// FK e deixando linhas orfas no SQL Server de dev compartilhado. Repete ate sobrar so a(s)
  /// raiz(es): a cada volta remove as FOLHAS (nos cujo Id nenhum nao-raiz restante referencia como
  /// pai), entao nunca tenta apagar um pai antes do proprio filho.
  /// </summary>
  private static async Task LimparAsync(int pedidoId, int agrupamentoId, params int[] componenteIds)
  {
    await using var db = NovoContexto();

    var idsDaEstrutura = await db.Estruturas.AsNoTracking()
        .Where(e => e.AgrupamentoId == agrupamentoId).Select(e => e.Id).ToListAsync();

    db.EstruturaRoteiros.RemoveRange(
        await db.EstruturaRoteiros.Where(r => idsDaEstrutura.Contains(r.EstruturaItemId)).ToListAsync());
    db.EstruturaMateriais.RemoveRange(
        await db.EstruturaMateriais.Where(m => idsDaEstrutura.Contains(m.EstruturaItemId)).ToListAsync());
    await db.SaveChangesAsync();

    var naoRaizes = await db.Estruturas
        .Where(e => e.AgrupamentoId == agrupamentoId && e.EstruturaPaiId != null).ToListAsync();
    while (naoRaizes.Count > 0)
    {
      var paisAindaPresentes = naoRaizes.Select(n => n.EstruturaPaiId!.Value).ToHashSet();
      var folhas = naoRaizes.Where(n => !paisAindaPresentes.Contains(n.Id)).ToList();
      db.Estruturas.RemoveRange(folhas);
      await db.SaveChangesAsync();
      naoRaizes = naoRaizes.Except(folhas).ToList();
    }

    db.Estruturas.RemoveRange(await db.Estruturas.Where(e => e.AgrupamentoId == agrupamentoId).ToListAsync());
    await db.SaveChangesAsync();

    db.Agrupamentos.RemoveRange(await db.Agrupamentos.Where(a => a.Id == agrupamentoId).ToListAsync());
    await db.SaveChangesAsync();

    db.Pedidos.RemoveRange(await db.Pedidos.Where(p => p.Id == pedidoId).ToListAsync());
    await db.SaveChangesAsync();

    if (componenteIds.Length > 0)
    {
      db.Componentes.RemoveRange(await db.Componentes.Where(c => componenteIds.Contains(c.Id)).ToListAsync());
      await db.SaveChangesAsync();
    }
  }

  [Fact]
  public async Task GravarArvoreAsync_grava_pai_e_filho_com_o_nivel_hierarquico_correto()
  {
    var prefixo = NovoPrefixo();
    await using var db = NovoContexto();
    var (pedidoId, agrupamentoId) = await NovoAgrupamentoAsync(db);
    var componenteRaiz = await NovoComponenteAsync(db, prefixo, "raiz");
    var componenteFilho = await NovoComponenteAsync(db, prefixo, "filho");

    try
    {
      var repo = new EstruturaRepository(db);
      var no = new NoParaGravar(
          ComponenteId: componenteRaiz, Descricao: null, Quantidade: 10m, RequerRelatorioDimensional: true,
          Materiais: [], Roteiro: [],
          Filhos: [new NoParaGravar(componenteFilho, null, 40m, false, [], [], [])]);

      var raizId = await repo.GravarArvoreAsync(agrupamentoId, null, no, CancellationToken.None);

      await using var dbLeitura = NovoContexto();
      var itens = await dbLeitura.Estruturas.AsNoTracking()
          .Where(e => e.AgrupamentoId == agrupamentoId).ToListAsync();

      var raiz = itens.Single(i => i.Id == raizId);
      Assert.Equal("Peca", raiz.NivelHierarquico);
      Assert.Null(raiz.EstruturaPaiId);
      Assert.True(raiz.RequerRelatorioDimensional);

      var filho = itens.Single(i => i.EstruturaPaiId == raizId);
      Assert.Equal("Item", filho.NivelHierarquico);
      Assert.Equal(40m, filho.Quantidade);
      Assert.False(filho.RequerRelatorioDimensional);
    }
    finally
    {
      await LimparAsync(pedidoId, agrupamentoId, componenteRaiz, componenteFilho);
    }
  }

  /// <summary>
  /// A mutacao que o brief da Task 3 manda medir: "remover a transacao e gravar no a no -&gt; tem
  /// de quebrar num caso com erro no meio da arvore". Forca o INSERT de EstruturaMaterial do FILHO
  /// a violar FK_EstruturaMaterial_Material (Id inexistente) DEPOIS que a raiz ja foi persistida
  /// pelo seu proprio SaveChangesAsync (dentro de GravarNo). Sem a transacao em volta da descida
  /// inteira, a raiz ficaria gravada mesmo com o resto da arvore falhando; com ela, nada sobra —
  /// e e essa segunda parte que este teste prova.
  /// </summary>
  [Fact]
  public async Task GravarArvoreAsync_e_atomico_erro_no_meio_da_arvore_nao_deixa_nada_gravado()
  {
    var prefixo = NovoPrefixo();
    await using var db = NovoContexto();
    var (pedidoId, agrupamentoId) = await NovoAgrupamentoAsync(db);
    var componenteRaiz = await NovoComponenteAsync(db, prefixo, "raiz");
    var componenteFilho = await NovoComponenteAsync(db, prefixo, "filho");

    try
    {
      var repo = new EstruturaRepository(db);
      const int materialInexistente = int.MaxValue - 1;
      var no = new NoParaGravar(
          ComponenteId: componenteRaiz, Descricao: null, Quantidade: 10m, RequerRelatorioDimensional: false,
          Materiais: [], Roteiro: [],
          Filhos:
          [
            new NoParaGravar(
                ComponenteId: componenteFilho, Descricao: null, Quantidade: 40m, RequerRelatorioDimensional: false,
                Materiais: [(materialInexistente, 1m)], Roteiro: [], Filhos: [])
          ]);

      await Assert.ThrowsAsync<DbUpdateException>(
          () => repo.GravarArvoreAsync(agrupamentoId, null, no, CancellationToken.None));

      await using var dbLeitura = NovoContexto();
      var sobrouAlgumaLinha = await dbLeitura.Estruturas.AsNoTracking()
          .AnyAsync(e => e.AgrupamentoId == agrupamentoId);

      Assert.False(sobrouAlgumaLinha);
    }
    finally
    {
      await LimparAsync(pedidoId, agrupamentoId, componenteRaiz, componenteFilho);
    }
  }

  /// <summary>
  /// `RemoverSubarvoreAsync` (Task 4) contra o SQL Server real. NAO esta no brief da Task 4 (que so
  /// pede `Application.Tests`, com o fake), pelo mesmo motivo do arquivo inteiro: o fake nao aplica
  /// FK nenhuma, entao a ordem "Material/Roteiro antes do no, filho antes de pai" pode estar
  /// invertida no fake sem quebrar nada — a Task 1 ja mordeu exatamente esse erro (FK) num teste, e
  /// o proprio brief da Task 4 cita esse historico ao descrever a ordem exigida. Sem este teste,
  /// trocar a ordem das duas `RemoveRange` (ou apagar so o `id` sem descer os niveis) so estouraria
  /// em producao, contra `DbUpdateException` de FK que nenhum teste de Application pode reproduzir.
  ///
  /// Arvore de 3 niveis (raiz -&gt; meio -&gt; folha), com Material e Roteiro no MEIO e na FOLHA — as
  /// duas tabelas que a ordem tem de esvaziar antes de apagar os proprios EstruturaItem. Apaga a
  /// partir do MEIO: raiz sobrevive, meio e folha (e os respectivos Material/Roteiro) somem.
  /// </summary>
  [Fact]
  public async Task RemoverSubarvoreAsync_apaga_material_roteiro_e_subarvore_sem_violar_FK()
  {
    var prefixo = NovoPrefixo();
    await using var db = NovoContexto();
    var (pedidoId, agrupamentoId) = await NovoAgrupamentoAsync(db);
    var componenteRaiz = await NovoComponenteAsync(db, prefixo, "raiz");
    var componenteMeio = await NovoComponenteAsync(db, prefixo, "meio");
    var componenteFolha = await NovoComponenteAsync(db, prefixo, "folha");

    var material = new Material
    { Codigo = $"{prefixo}-m", Descricao = "Material de teste", UnidadeMedida = "UN", Ativo = true };
    var setor = new Setor { Nome = $"{prefixo}-s", Ativo = true };
    db.Materiais.Add(material);
    db.Setores.Add(setor);
    await db.SaveChangesAsync();

    try
    {
      var repo = new EstruturaRepository(db);
      var no = new NoParaGravar(
          ComponenteId: componenteRaiz, Descricao: null, Quantidade: 10m, RequerRelatorioDimensional: true,
          Materiais: [], Roteiro: [],
          Filhos:
          [
            new NoParaGravar(
                ComponenteId: componenteMeio, Descricao: null, Quantidade: 40m, RequerRelatorioDimensional: false,
                Materiais: [(material.Id, 1m)], Roteiro: [(setor.Id, 1)],
                Filhos:
                [
                  new NoParaGravar(
                      ComponenteId: componenteFolha, Descricao: null, Quantidade: 5m,
                      RequerRelatorioDimensional: false,
                      Materiais: [(material.Id, 2m)], Roteiro: [(setor.Id, 1)], Filhos: [])
                ])
          ]);

      var raizId = await repo.GravarArvoreAsync(agrupamentoId, null, no, CancellationToken.None);

      await using var dbLeitura = NovoContexto();
      var meioId = (await dbLeitura.Estruturas.AsNoTracking()
          .SingleAsync(e => e.AgrupamentoId == agrupamentoId && e.EstruturaPaiId == raizId)).Id;
      var folhaId = (await dbLeitura.Estruturas.AsNoTracking()
          .SingleAsync(e => e.AgrupamentoId == agrupamentoId && e.EstruturaPaiId == meioId)).Id;

      await using var dbExclusao = NovoContexto();
      var repoExclusao = new EstruturaRepository(dbExclusao);
      await repoExclusao.RemoverSubarvoreAsync(meioId, CancellationToken.None);

      await using var dbVerificacao = NovoContexto();
      var idsRestantes = await dbVerificacao.Estruturas.AsNoTracking()
          .Where(e => e.AgrupamentoId == agrupamentoId).Select(e => e.Id).ToListAsync();
      Assert.Equal([raizId], idsRestantes);

      var materiaisRestantes = await dbVerificacao.EstruturaMateriais.AsNoTracking()
          .Where(m => m.EstruturaItemId == meioId || m.EstruturaItemId == folhaId).CountAsync();
      var roteirosRestantes = await dbVerificacao.EstruturaRoteiros.AsNoTracking()
          .Where(r => r.EstruturaItemId == meioId || r.EstruturaItemId == folhaId).CountAsync();
      Assert.Equal(0, materiaisRestantes);
      Assert.Equal(0, roteirosRestantes);
    }
    finally
    {
      await LimparAsync(pedidoId, agrupamentoId, componenteRaiz, componenteMeio, componenteFolha);
      await using var dbLimpeza = NovoContexto();
      dbLimpeza.Materiais.RemoveRange(await dbLimpeza.Materiais.Where(m => m.Id == material.Id).ToListAsync());
      dbLimpeza.Setores.RemoveRange(await dbLimpeza.Setores.Where(s => s.Id == setor.Id).ToListAsync());
      await dbLimpeza.SaveChangesAsync();
    }
  }

  /// <summary>
  /// M6 da review da Task 4: `CK_EstruturaItem_NaoAutoReferencia` so impede um no apontar pra SI
  /// MESMO — nao impede um ciclo mais longo entre nos distintos. Monta A -&gt; B validos (dois
  /// `SaveChangesAsync`, sem violar nenhuma CHECK), depois fecha o ciclo por fora da aplicacao com
  /// um UPDATE direto: A passa a apontar pra B (que ja aponta pra A). Sem o conjunto de visitados
  /// que este fix pass acrescentou, `RemoverSubarvoreAsync(A)` entraria num `while` sem fim; com a
  /// guarda, lanca <see cref="SubarvoreCiclicaException"/> — e e essa excecao, nao um travamento,
  /// que este teste prova.
  /// </summary>
  [Fact]
  public async Task RemoverSubarvoreAsync_com_ciclo_nos_dados_lanca_em_vez_de_travar()
  {
    var prefixo = NovoPrefixo();
    await using var db = NovoContexto();
    var (pedidoId, agrupamentoId) = await NovoAgrupamentoAsync(db);
    var componenteA = await NovoComponenteAsync(db, prefixo, "a");
    var componenteB = await NovoComponenteAsync(db, prefixo, "b");

    try
    {
      var repo = new EstruturaRepository(db);
      var noA = new NoParaGravar(
          ComponenteId: componenteA, Descricao: null, Quantidade: 1m, RequerRelatorioDimensional: false,
          Materiais: [], Roteiro: [],
          Filhos: [new NoParaGravar(componenteB, null, 1m, false, [], [], [])]);
      var idA = await repo.GravarArvoreAsync(agrupamentoId, null, noA, CancellationToken.None);

      await using var dbLeitura = NovoContexto();
      var idB = (await dbLeitura.Estruturas.AsNoTracking()
          .SingleAsync(e => e.AgrupamentoId == agrupamentoId && e.EstruturaPaiId == idA)).Id;

      // Fecha o ciclo por fora da app: A passa a apontar pra B. Muda tambem NivelHierarquico pra
      // "Item" — CK_EstruturaItem_PecaSemPai exige EstruturaPaiId nulo so quando e "Peca".
      await using var dbCiclo = NovoContexto();
      var linhaA = await dbCiclo.Estruturas.SingleAsync(e => e.Id == idA);
      linhaA.EstruturaPaiId = idB;
      linhaA.NivelHierarquico = "Item";
      await dbCiclo.SaveChangesAsync();

      await using var dbExclusao = NovoContexto();
      var repoExclusao = new EstruturaRepository(dbExclusao);
      await Assert.ThrowsAsync<SubarvoreCiclicaException>(
          () => repoExclusao.RemoverSubarvoreAsync(idA, CancellationToken.None));
    }
    finally
    {
      // Desfaz o ciclo antes de limpar — senao LimparAsync (que tambem so sabe andar num grafo
      // aciclico, mesma classe de risco do M3) tropeca do mesmo jeito.
      await using var dbDesfaz = NovoContexto();
      var linhaA = await dbDesfaz.Estruturas.SingleOrDefaultAsync(e => e.AgrupamentoId == agrupamentoId && e.EstruturaPaiId != null && e.ComponenteId == componenteA);
      if (linhaA is not null)
      {
        linhaA.EstruturaPaiId = null;
        linhaA.NivelHierarquico = "Peca";
        await dbDesfaz.SaveChangesAsync();
      }
      await LimparAsync(pedidoId, agrupamentoId, componenteA, componenteB);
    }
  }

  [Fact]
  public async Task LerReceitaCompletaAsync_le_as_tres_tabelas_padrao()
  {
    var prefixo = NovoPrefixo();
    await using var db = NovoContexto();
    var pai = await NovoComponenteAsync(db, prefixo, "pai");
    var filho = await NovoComponenteAsync(db, prefixo, "filho");

    var material = new Material
    {
      Codigo = $"{prefixo}-m", Descricao = "Material de teste", UnidadeMedida = "UN", Ativo = true,
    };
    var setor = new Setor { Nome = $"{prefixo}-s", Ativo = true };
    db.Materiais.Add(material);
    db.Setores.Add(setor);
    await db.SaveChangesAsync();

    db.FilhosPadrao.Add(new ComponenteFilhoPadrao
    { ComponentePaiId = pai, ComponenteFilhoId = filho, QuantidadePadrao = 3m });
    db.MateriaisPadrao.Add(new ComponenteMaterialPadrao
    { ComponenteId = pai, MaterialId = material.Id, QuantidadePadrao = 2m });
    db.RoteirosPadrao.Add(new ComponenteRoteiroPadrao { ComponenteId = pai, SetorId = setor.Id, Ordem = 1 });
    await db.SaveChangesAsync();

    try
    {
      var repo = new EstruturaRepository(db);
      var (filhos, materiais, roteiro) = await repo.LerReceitaCompletaAsync(CancellationToken.None);

      Assert.Contains((pai, filho, 3m), filhos);
      Assert.Contains((pai, material.Id, 2m), materiais);
      Assert.Contains((pai, setor.Id, 1), roteiro);
    }
    finally
    {
      await using var dbLimpeza = NovoContexto();
      dbLimpeza.FilhosPadrao.RemoveRange(
          await dbLimpeza.FilhosPadrao.Where(f => f.ComponentePaiId == pai).ToListAsync());
      dbLimpeza.MateriaisPadrao.RemoveRange(
          await dbLimpeza.MateriaisPadrao.Where(m => m.ComponenteId == pai).ToListAsync());
      dbLimpeza.RoteirosPadrao.RemoveRange(
          await dbLimpeza.RoteirosPadrao.Where(r => r.ComponenteId == pai).ToListAsync());
      await dbLimpeza.SaveChangesAsync();

      dbLimpeza.Materiais.RemoveRange(await dbLimpeza.Materiais.Where(m => m.Id == material.Id).ToListAsync());
      dbLimpeza.Setores.RemoveRange(await dbLimpeza.Setores.Where(s => s.Id == setor.Id).ToListAsync());
      dbLimpeza.Componentes.RemoveRange(
          await dbLimpeza.Componentes.Where(c => c.Id == pai || c.Id == filho).ToListAsync());
      await dbLimpeza.SaveChangesAsync();
    }
  }
}

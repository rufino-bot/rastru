using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// <see cref="SeparacaoRepository"/> contra o SQL Server real. Cada teste cria a SUA arvore (prefixo
/// unico) e afirma so sobre os Ids dela. Mesma <c>[Collection]</c> do mapeamento do livro, porque a
/// arvore escreve em `Componente` e `Material` — as tabelas que aquela colecao serializa.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class SeparacaoRepositoryTests : TesteComBanco
{
  private sealed record Cenario(ArvoreDeTesteNoBanco Arvore, int PecaId, int OutraPecaId, int Material1, int Material2);

  private async Task NoCenarioAsync(Func<Cenario, Task> corpo)
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "seprepo");
    try
    {
      var peca = await arvore.NovaPecaAsync(db, 10m);
      var outra = await arvore.NovaPecaAsync(db, 5m);
      var m1 = await arvore.NovoMaterialAsync(db, "Parafuso");
      var m2 = await arvore.NovoMaterialAsync(db, "Porca");
      await corpo(new Cenario(arvore, peca, outra, m1, m2));
    }
    finally { await arvore.LimparAsync(NovoContexto); }
  }

  private static MaterialSeparacao Separacao(Cenario c, int noId, int materialId, decimal quantidade) => new()
  {
    EstruturaItemId = noId, MaterialId = materialId, Tipo = TiposDeSeparacao.Separacao,
    Quantidade = quantidade, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private static MaterialSeparacao Estorno(Cenario c, MaterialSeparacao original) => new()
  {
    EstruturaItemId = original.EstruturaItemId, MaterialId = original.MaterialId, Tipo = TiposDeSeparacao.Estorno,
    Quantidade = original.Quantidade, EstornoDeId = original.Id, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  [Fact]
  public Task GravarAsync_grava_todas_as_linhas_e_preenche_os_ids() => NoCenarioAsync(async c =>
  {
    var linhas = new List<MaterialSeparacao>
    {
      Separacao(c, c.PecaId, c.Material1, 2m), Separacao(c, c.PecaId, c.Material2, 3.5m),
    };

    await using var db = NovoContexto();
    await new SeparacaoRepository(db).GravarAsync(linhas, CancellationToken.None);

    Assert.All(linhas, l => Assert.True(l.Id > 0));
    Assert.NotEqual(linhas[0].Id, linhas[1].Id);

    await using var leitura = NovoContexto();
    var gravadas = await leitura.Separacoes.AsNoTracking().Where(s => s.EstruturaItemId == c.PecaId).ToListAsync();
    Assert.Equal(2, gravadas.Count);
    Assert.Equal(3.5m, gravadas.Single(s => s.MaterialId == c.Material2).Quantidade);
  });

  [Fact]
  public Task ListarLinhasAsync_traz_so_os_nos_pedidos() => NoCenarioAsync(async c =>
  {
    await using (var escrita = NovoContexto())
      await new SeparacaoRepository(escrita).GravarAsync(
          [Separacao(c, c.PecaId, c.Material1, 1m), Separacao(c, c.OutraPecaId, c.Material1, 2m)], CancellationToken.None);

    await using var db = NovoContexto();
    var repo = new SeparacaoRepository(db);

    var so1 = await repo.ListarLinhasAsync([c.PecaId], CancellationToken.None);
    var ambos = await repo.ListarLinhasAsync([c.PecaId, c.OutraPecaId], CancellationToken.None);
    var nenhum = await repo.ListarLinhasAsync([], CancellationToken.None);

    Assert.Equal(c.PecaId, Assert.Single(so1).EstruturaItemId);
    Assert.Equal(2, ambos.Count);
    Assert.Empty(nenhum);
  });

  [Fact]
  public Task SubstituirPlanejadoAsync_troca_a_lista_inteira() => NoCenarioAsync(async c =>
  {
    await using (var db = NovoContexto())
      await c.Arvore.MaterialNoNoAsync(db, c.PecaId, c.Material1);
    await using (var outro = NovoContexto())
      await c.Arvore.MaterialNoNoAsync(outro, c.OutraPecaId, c.Material1);

    await using var contexto = NovoContexto();
    var repo = new SeparacaoRepository(contexto);
    await repo.SubstituirPlanejadoAsync(c.PecaId, [(c.Material2, 3m)], CancellationToken.None);

    await using var leitura = NovoContexto();
    var planejado = await new SeparacaoRepository(leitura).ListarPlanejadoAsync([c.PecaId], CancellationToken.None);
    var linha = Assert.Single(planejado);
    Assert.Equal(c.Material2, linha.MaterialId);
    Assert.Equal(3m, linha.Quantidade);

    // O planejado de OUTRO no nao e tocado.
    var daOutra = await new SeparacaoRepository(leitura).ListarPlanejadoAsync([c.OutraPecaId], CancellationToken.None);
    Assert.Equal(c.Material1, Assert.Single(daOutra).MaterialId);
  });

  [Fact]
  public Task SubstituirPlanejadoAsync_com_o_mesmo_material_e_outra_quantidade_deixa_uma_linha_so() => NoCenarioAsync(async c =>
  {
    // UQ_EstruturaMaterial (no, material): apagar e inserir a mesma chave no mesmo SaveChanges.
    await using (var db = NovoContexto())
      await c.Arvore.MaterialNoNoAsync(db, c.PecaId, c.Material1);

    await using var contexto = NovoContexto();
    await new SeparacaoRepository(contexto).SubstituirPlanejadoAsync(c.PecaId, [(c.Material1, 5m)], CancellationToken.None);

    await using var leitura = NovoContexto();
    var linha = Assert.Single(await new SeparacaoRepository(leitura).ListarPlanejadoAsync([c.PecaId], CancellationToken.None));
    Assert.Equal(c.Material1, linha.MaterialId);
    Assert.Equal(5m, linha.Quantidade);
  });

  [Fact]
  public Task SubstituirPlanejadoAsync_com_lista_vazia_limpa() => NoCenarioAsync(async c =>
  {
    await using (var db = NovoContexto())
      await c.Arvore.MaterialNoNoAsync(db, c.PecaId, c.Material1);

    await using var contexto = NovoContexto();
    await new SeparacaoRepository(contexto).SubstituirPlanejadoAsync(c.PecaId, [], CancellationToken.None);

    await using var leitura = NovoContexto();
    Assert.Empty(await new SeparacaoRepository(leitura).ListarPlanejadoAsync([c.PecaId], CancellationToken.None));
  });

  [Fact]
  public Task GravarEstornoAsync_devolve_false_no_segundo_estorno_da_mesma_linha() => NoCenarioAsync(async c =>
  {
    var original = Separacao(c, c.PecaId, c.Material1, 4m);
    await using (var escrita = NovoContexto())
      await new SeparacaoRepository(escrita).GravarAsync([original], CancellationToken.None);

    await using var primeiro = NovoContexto();
    await using var segundo = NovoContexto();
    var estornoA = Estorno(c, original);
    var estornoB = Estorno(c, original);

    var gravouA = await new SeparacaoRepository(primeiro).GravarEstornoAsync(estornoA, CancellationToken.None);
    var gravouB = await new SeparacaoRepository(segundo).GravarEstornoAsync(estornoB, CancellationToken.None);

    Assert.True(gravouA);
    Assert.False(gravouB);
    Assert.True(estornoA.Id > 0);

    // So um estorno existe, e o contexto que perdeu a corrida continua usavel (a entidade foi desanexada).
    await using var leitura = NovoContexto();
    Assert.Equal(1, await leitura.Separacoes.CountAsync(s => s.EstornoDeId == original.Id));
    Assert.DoesNotContain(segundo.ChangeTracker.Entries<MaterialSeparacao>(), e => e.Entity == estornoB);
    Assert.True(await new SeparacaoRepository(segundo).NoExisteAsync(c.PecaId, CancellationToken.None));
  });

  [Fact]
  public Task GravarEstornoAsync_propaga_o_erro_que_nao_e_de_unicidade() => NoCenarioAsync(async c =>
  {
    // EstornoDeId inexistente: viola a FK (547), que nao e a corrida de estorno e nao pode virar `false`.
    var orfao = new MaterialSeparacao
    {
      EstruturaItemId = c.PecaId, MaterialId = c.Material1, Tipo = TiposDeSeparacao.Estorno, Quantidade = 1m,
      EstornoDeId = int.MaxValue, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    };

    await using var db = NovoContexto();
    await Assert.ThrowsAsync<DbUpdateException>(
        () => new SeparacaoRepository(db).GravarEstornoAsync(orfao, CancellationToken.None));
  });

  [Fact]
  public Task FoiEstornadaAsync_ve_o_estorno() => NoCenarioAsync(async c =>
  {
    var original = Separacao(c, c.PecaId, c.Material1, 1m);
    var outra = Separacao(c, c.PecaId, c.Material1, 1m);
    await using (var escrita = NovoContexto())
    {
      var repo = new SeparacaoRepository(escrita);
      await repo.GravarAsync([original, outra], CancellationToken.None);
      Assert.True(await repo.GravarEstornoAsync(Estorno(c, original), CancellationToken.None));
    }

    await using var db = NovoContexto();
    var leitor = new SeparacaoRepository(db);
    Assert.True(await leitor.FoiEstornadaAsync(original.Id, CancellationToken.None));
    Assert.False(await leitor.FoiEstornadaAsync(outra.Id, CancellationToken.None));

    var obtida = await leitor.ObterLinhaAsync(original.Id, CancellationToken.None);
    Assert.Equal(c.PecaId, obtida!.EstruturaItemId);
    Assert.Null(await leitor.ObterLinhaAsync(int.MaxValue, CancellationToken.None));
  });

  [Fact]
  public Task ListarMateriaisAsync_traz_o_nome_do_setor_de_estoque() => NoCenarioAsync(async c =>
  {
    int setorInativo;
    await using (var db = NovoContexto())
    {
      setorInativo = await c.Arvore.NovoSetorAsync(db, ativo: false);
      var material = await db.Materiais.SingleAsync(m => m.Id == c.Material2);
      material.SetorDeEstoqueId = setorInativo;
      await db.SaveChangesAsync();
    }

    await using var leitura = NovoContexto();
    var materiais = await new SeparacaoRepository(leitura).ListarMateriaisAsync(
        [c.Material1, c.Material2], CancellationToken.None);

    var almoxarifado = materiais.Single(m => m.Id == c.Material1);
    Assert.Null(almoxarifado.SetorDeEstoqueId);
    Assert.Null(almoxarifado.SetorDeEstoqueNome);
    Assert.Equal("UN", almoxarifado.UnidadeMedida);
    Assert.True(almoxarifado.Ativo);

    var noSetor = materiais.Single(m => m.Id == c.Material2);
    Assert.Equal(setorInativo, noSetor.SetorDeEstoqueId);
    var nomeDoSetor = (await leitura.Setores.AsNoTracking().SingleAsync(s => s.Id == setorInativo)).Nome;
    Assert.Equal(nomeDoSetor, noSetor.SetorDeEstoqueNome);

    Assert.Empty(await new SeparacaoRepository(leitura).ListarMateriaisAsync([], CancellationToken.None));
  });

  [Fact]
  public Task ListarNosComMaterialDoAlmoxarifadoAsync_ignora_no_so_com_material_de_Setor() => NoCenarioAsync(async c =>
  {
    await using (var db = NovoContexto())
    {
      var setor = await c.Arvore.NovoSetorAsync(db);
      var material = await db.Materiais.SingleAsync(m => m.Id == c.Material2);
      material.SetorDeEstoqueId = setor;
      await db.SaveChangesAsync();

      // PecaId: so material de Setor. OutraPecaId: um de Setor e um do almoxarifado.
      await c.Arvore.MaterialNoNoAsync(db, c.PecaId, c.Material2);
      await c.Arvore.MaterialNoNoAsync(db, c.OutraPecaId, c.Material2);
      await c.Arvore.MaterialNoNoAsync(db, c.OutraPecaId, c.Material1);
    }

    await using var leitura = NovoContexto();
    var nos = await new SeparacaoRepository(leitura).ListarNosComMaterialDoAlmoxarifadoAsync(
        [c.PecaId, c.OutraPecaId], CancellationToken.None);

    Assert.Equal([c.OutraPecaId], nos.ToArray());
  });
}

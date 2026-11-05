using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// A arvore que a conta da baixa le, contra o SQL Server real (secao 4.6 da spec da Fase 5B-1): a cadeia do no ate a
/// Peca do topo, do PAI para cima e nessa ordem (e o que `CalculoDaBaixa.Planejar` sobe nivel a nivel para a trava no
/// topo), e a subarvore em ordem de Id. Tres niveis e dois ramos, para a cadeia ter mais de um elemento e a subarvore de
/// um filho nao ser a da Peca. Cada teste cria a SUA arvore e afirma so sobre os Ids dela. Mesma <c>[Collection]</c> do
/// mapeamento do livro, porque a arvore escreve em `Componente`.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class BaixaRepositoryTests : TesteComBanco
{
  private sealed record Cenario(int Peca, int Filho, int Neto, int OutroFilho);

  private async Task NoCenarioAsync(Func<Cenario, BaixaRepository, Task> corpo)
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "baixarepo");
    try
    {
      var peca = await arvore.NovaPecaAsync(db, 10m);
      var filho = await arvore.NovoItemAsync(db, peca, 20m, 2m);
      var neto = await arvore.NovoItemAsync(db, filho, 20m, 1m);
      var outro = await arvore.NovoItemAsync(db, peca, 10m, 1m);
      await using var leitura = NovoContexto();
      await corpo(new Cenario(peca, filho, neto, outro), new BaixaRepository(leitura));
    }
    finally { await arvore.LimparAsync(NovoContexto); }
  }

  [Fact]
  public Task Cadeia_vai_do_pai_ate_a_Peca_do_topo_nessa_ordem() => NoCenarioAsync(async (c, repo) =>
  {
    Assert.Equal([c.Filho, c.Peca], await repo.ListarCadeiaAteOTopoAsync(c.Neto, CancellationToken.None));
    Assert.Equal([c.Peca], await repo.ListarCadeiaAteOTopoAsync(c.OutroFilho, CancellationToken.None));
  });

  [Fact]
  public Task Cadeia_da_Peca_e_vazia() => NoCenarioAsync(async (c, repo) =>
      Assert.Empty(await repo.ListarCadeiaAteOTopoAsync(c.Peca, CancellationToken.None)));

  [Fact]
  public Task Subarvore_traz_o_no_e_todos_os_descendentes_em_ordem_de_Id() => NoCenarioAsync(async (c, repo) =>
  {
    Assert.Equal(
        new[] { c.Peca, c.Filho, c.Neto, c.OutroFilho }.Order(),
        await repo.ListarSubarvoreAsync(c.Peca, CancellationToken.None));
    Assert.Equal([c.Filho, c.Neto], await repo.ListarSubarvoreAsync(c.Filho, CancellationToken.None));
  });

  [Fact]
  public Task No_inexistente_tem_cadeia_e_subarvore_vazias() => NoCenarioAsync(async (_, repo) =>
  {
    Assert.Empty(await repo.ListarCadeiaAteOTopoAsync(int.MaxValue, CancellationToken.None));
    Assert.Empty(await repo.ListarSubarvoreAsync(int.MaxValue, CancellationToken.None));
  });
}

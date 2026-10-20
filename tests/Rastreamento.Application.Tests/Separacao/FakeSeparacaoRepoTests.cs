using Rastreamento.Application.Tests.Estrutura;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Separacao;

/// <summary>O fake e infraestrutura de teste, mas os dois modos dele sao contrato para quem o usa.</summary>
public class FakeSeparacaoRepoTests
{
  private static MaterialSeparacao Linha(string tipo, int? estornoDeId = null) => new()
  {
    EstruturaItemId = 1, MaterialId = 7, Tipo = tipo, Quantidade = 2m, EstornoDeId = estornoDeId,
  };

  [Fact]
  public async Task Com_o_FakeEstruturaRepo_le_e_escreve_o_planejado_dele()
  {
    var estruturas = new FakeEstruturaRepo();
    estruturas.Materiais.Add(new EstruturaMaterial { Id = 5, EstruturaItemId = 1, MaterialId = 7, Quantidade = 3m });
    var repo = new FakeSeparacaoRepo(estruturas);

    Assert.Same(estruturas.Materiais, repo.Planejado);
    Assert.Equal(7, Assert.Single(await repo.ListarPlanejadoAsync([1], CancellationToken.None)).MaterialId);

    await repo.SubstituirPlanejadoAsync(1, [(8, 4m)], CancellationToken.None);

    var gravado = Assert.Single(estruturas.Materiais);
    Assert.Equal(8, gravado.MaterialId);
    Assert.True(gravado.Id > 5);
  }

  [Fact]
  public async Task Sem_o_FakeEstruturaRepo_usa_o_planejado_proprio()
  {
    var repo = new FakeSeparacaoRepo();
    repo.Planejado.Add(new EstruturaMaterial { Id = 1, EstruturaItemId = 1, MaterialId = 7, Quantidade = 3m });

    await repo.SubstituirPlanejadoAsync(1, [], CancellationToken.None);

    Assert.Empty(repo.Planejado);
  }

  [Fact]
  public async Task Estorno_concorrente_devolve_false_uma_vez_so()
  {
    var repo = new FakeSeparacaoRepo();
    var original = Linha(TiposDeSeparacao.Separacao);
    await repo.GravarAsync([original], CancellationToken.None);
    repo.EstornoConcorrenteNaProximaGravacao = true;

    Assert.False(await repo.GravarEstornoAsync(Linha(TiposDeSeparacao.Estorno, original.Id), CancellationToken.None));
    Assert.Single(repo.Linhas);

    Assert.True(await repo.GravarEstornoAsync(Linha(TiposDeSeparacao.Estorno, original.Id), CancellationToken.None));
    Assert.Equal(2, repo.Linhas.Count);

    // Como o indice unico do banco: um segundo estorno da mesma linha perde.
    Assert.False(await repo.GravarEstornoAsync(Linha(TiposDeSeparacao.Estorno, original.Id), CancellationToken.None));
    Assert.Equal(2, repo.Gravacoes);
  }
}

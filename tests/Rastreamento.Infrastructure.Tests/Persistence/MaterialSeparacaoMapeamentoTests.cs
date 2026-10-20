using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// `dbo.MaterialSeparacao` (spec da Fase 4, secao 3.2) e `Material.SetorDeEstoqueId` (secao 3.1) contra
/// o SQL Server real: o mapeamento faz a volta completa, e cada restricao do livro recusa um caso. Cada
/// caso viola UMA restricao so — o `Tipo` e o `EstornoDeId` dos casos de CHECK sao escolhidos para que
/// nenhuma outra restricao dispare antes.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class MaterialSeparacaoMapeamentoTests : TesteComBanco
{
  private sealed record Cenario(ArvoreDeTesteNoBanco Arvore, int PecaId, int MaterialId);

  private static async Task<Cenario> NovoCenarioAsync(RastreamentoDbContext db)
  {
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "sepmat");
    var peca = await arvore.NovaPecaAsync(db, 10m);
    var material = await arvore.NovoMaterialAsync(db, "Parafuso de teste");
    return new Cenario(arvore, peca, material);
  }

  private async Task NoCenarioAsync(Func<Cenario, Task> corpo)
  {
    await using var db = NovoContexto();
    var c = await NovoCenarioAsync(db);
    try { await corpo(c); }
    finally { await c.Arvore.LimparAsync(NovoContexto); }
  }

  private static MaterialSeparacao Separacao(Cenario c, decimal quantidade = 1m) => new()
  {
    EstruturaItemId = c.PecaId, MaterialId = c.MaterialId, Tipo = TiposDeSeparacao.Separacao,
    Quantidade = quantidade, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private static MaterialSeparacao Estorno(Cenario c, int? estornoDeId, decimal quantidade = 1m) => new()
  {
    EstruturaItemId = c.PecaId, MaterialId = c.MaterialId, Tipo = TiposDeSeparacao.Estorno,
    Quantidade = quantidade, EstornoDeId = estornoDeId, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private async Task<int> GravarAsync(MaterialSeparacao linha)
  {
    await using var db = NovoContexto();
    db.Separacoes.Add(linha);
    await db.SaveChangesAsync();
    return linha.Id;
  }

  private async Task AfirmarRecusaAsync(MaterialSeparacao linha, string restricao)
  {
    await using var db = NovoContexto();
    db.Separacoes.Add(linha);
    var erro = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    Assert.Contains(restricao, erro.InnerException?.Message ?? erro.Message);
  }

  [Fact]
  public Task Separacao_faz_a_volta_completa() => NoCenarioAsync(async c =>
  {
    var linha = Separacao(c, 2.5025m);
    var id = await GravarAsync(linha);

    await using var leitura = NovoContexto();
    var lida = await leitura.Separacoes.AsNoTracking().SingleAsync(s => s.Id == id);
    Assert.Equal(c.PecaId, lida.EstruturaItemId);
    Assert.Equal(c.MaterialId, lida.MaterialId);
    Assert.Equal(TiposDeSeparacao.Separacao, lida.Tipo);
    Assert.Equal(2.5025m, lida.Quantidade);
    Assert.Null(lida.EstornoDeId);
    Assert.Equal(c.Arvore.AutorId, lida.UsuarioId);
    // DataHora explicita: ValueGeneratedOnAdd nao a sobrescreve quando o valor nao e o default de DateTime.
    Assert.Equal(linha.DataHora, lida.DataHora, TimeSpan.FromSeconds(1));
  });

  [Fact]
  public Task Estorno_aponta_a_separacao_original() => NoCenarioAsync(async c =>
  {
    var original = await GravarAsync(Separacao(c, 3m));
    var estorno = await GravarAsync(Estorno(c, original, 3m));

    await using var leitura = NovoContexto();
    var lido = await leitura.Separacoes.AsNoTracking().SingleAsync(s => s.Id == estorno);
    Assert.Equal(TiposDeSeparacao.Estorno, lido.Tipo);
    Assert.Equal(original, lido.EstornoDeId);
    Assert.Equal(3m, lido.Quantidade);
  });

  [Fact]
  public Task Segundo_estorno_da_mesma_linha_viola_UX_MaterialSeparacao_EstornoDe() => NoCenarioAsync(async c =>
  {
    var original = await GravarAsync(Separacao(c, 3m));
    await GravarAsync(Estorno(c, original, 3m));

    await AfirmarRecusaAsync(Estorno(c, original, 3m), "UX_MaterialSeparacao_EstornoDe");
  });

  [Fact]
  public Task Estorno_sem_EstornoDeId_viola_CK_MaterialSeparacao_EstornoApontaOriginal() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(Estorno(c, null), "CK_MaterialSeparacao_EstornoApontaOriginal"));

  [Fact]
  public Task Separacao_com_EstornoDeId_viola_CK_MaterialSeparacao_EstornoApontaOriginal() => NoCenarioAsync(async c =>
  {
    var original = await GravarAsync(Separacao(c));
    var comApontamento = Separacao(c);
    comApontamento.EstornoDeId = original;

    await AfirmarRecusaAsync(comApontamento, "CK_MaterialSeparacao_EstornoApontaOriginal");
  });

  [Fact]
  public Task Quantidade_zero_viola_CK_MaterialSeparacao_QuantidadePositiva() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(Separacao(c, 0m), "CK_MaterialSeparacao_QuantidadePositiva"));

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task SetorDeEstoqueId_do_Material_faz_a_volta_completa(bool noSetor)
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "fica-em");
    try
    {
      var setorId = await arvore.NovoSetorAsync(db);
      var materialId = await arvore.NovoMaterialAsync(db, "Chapa de teste");
      var material = await db.Materiais.SingleAsync(m => m.Id == materialId);
      material.SetorDeEstoqueId = noSetor ? setorId : null;
      await db.SaveChangesAsync();

      await using var leitura = NovoContexto();
      var lido = await leitura.Materiais.AsNoTracking().SingleAsync(m => m.Id == materialId);
      Assert.Equal(noSetor ? setorId : null, lido.SetorDeEstoqueId);
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }
}

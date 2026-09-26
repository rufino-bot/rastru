using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// `EstruturaItem.QuantidadePorPai` contra o SQL Server real: o mapeamento guarda as quatro casas, e
/// `CK_EstruturaItem_QuantidadePorPai` recusa Item sem razao, razao zero e Peca com razao (regra 26).
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class QuantidadePorPaiMapeamentoTests : TesteComBanco
{
  [Fact]
  public async Task Item_grava_e_le_a_razao_com_quatro_casas()
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "qpp");
    try
    {
      var peca = await arvore.NovaPecaAsync(db, 10m);
      var item = await arvore.NovoItemAsync(db, peca, 25.025m, 2.5025m);

      await using var leitura = NovoContexto();
      var lido = await leitura.Estruturas.AsNoTracking().SingleAsync(e => e.Id == item);
      var pecaLida = await leitura.Estruturas.AsNoTracking().SingleAsync(e => e.Id == peca);
      Assert.Equal(2.5025m, lido.QuantidadePorPai);
      Assert.Null(pecaLida.QuantidadePorPai);
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }

  [Theory]
  [InlineData("Item", null)]
  [InlineData("Item", "0")]
  [InlineData("Peca", "1")]
  public async Task Banco_recusa_razao_incoerente_com_o_nivel(string nivel, string? razao)
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "qpp");
    try
    {
      var peca = await arvore.NovaPecaAsync(db, 10m);
      await using var escrita = NovoContexto();
      escrita.Estruturas.Add(new EstruturaItem
      {
        AgrupamentoId = arvore.AgrupamentoId,
        ComponenteId = arvore.ComponenteId,
        EstruturaPaiId = nivel == "Item" ? peca : null,
        NivelHierarquico = nivel,
        Quantidade = 5m,
        QuantidadePorPai = razao is null ? null : decimal.Parse(razao, System.Globalization.CultureInfo.InvariantCulture),
      });

      var erro = await Assert.ThrowsAsync<DbUpdateException>(() => escrita.SaveChangesAsync());
      Assert.Contains("CK_EstruturaItem_QuantidadePorPai", erro.InnerException!.Message);
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }
}

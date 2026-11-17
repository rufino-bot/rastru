using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// `dbo.Embalagem` (spec do Panorama, secao 3.1) contra o SQL Server real: o mapeamento faz a volta
/// completa, o `CK_Embalagem_Tipo` recusa um tipo desconhecido e o indice `IX_Embalagem_EstruturaItem`
/// existe com as duas colunas, `Id` descendente. As afirmacoes sao so sobre as linhas do proprio teste.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class EmbalagemMapeamentoTests : TesteComBanco
{
  private async Task NoCenarioAsync(Func<ArvoreDeTesteNoBanco, int, Task> corpo)
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "embal");
    try
    {
      var pecaId = await arvore.NovaPecaAsync(db, 3m);
      await corpo(arvore, pecaId);
    }
    finally { await arvore.LimparAsync(NovoContexto); }
  }

  [Fact]
  public Task Embalagem_grava_e_le_as_colunas() => NoCenarioAsync(async (arvore, pecaId) =>
  {
    var linha = new Embalagem
    {
      EstruturaItemId = pecaId, Tipo = TiposDeEmbalagem.Marcar, DataHora = DateTime.UtcNow, UsuarioId = arvore.AutorId,
    };
    await using (var escrita = NovoContexto())
    {
      escrita.Embalagens.Add(linha);
      await escrita.SaveChangesAsync();
    }

    await using var leitura = NovoContexto();
    var lida = await leitura.Embalagens.AsNoTracking().SingleAsync(e => e.Id == linha.Id);
    Assert.Equal(pecaId, lida.EstruturaItemId);
    Assert.Equal(TiposDeEmbalagem.Marcar, lida.Tipo);
    Assert.Equal(linha.DataHora, lida.DataHora, TimeSpan.FromSeconds(1));
    Assert.Equal(arvore.AutorId, lida.UsuarioId);
  });

  [Fact]
  public Task Tipo_fora_do_check_e_recusado_pelo_banco() => NoCenarioAsync(async (arvore, pecaId) =>
  {
    await using var db = NovoContexto();
    db.Embalagens.Add(new Embalagem
    {
      EstruturaItemId = pecaId, Tipo = "Outro", DataHora = DateTime.UtcNow, UsuarioId = arvore.AutorId,
    });
    var erro = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    Assert.Contains("CK_Embalagem_Tipo", erro.InnerException?.Message ?? erro.Message);
  });

  [Fact]
  public async Task Indice_IX_Embalagem_EstruturaItem_existe()
  {
    await using var db = NovoContexto();
    var colunas = await db.Database.SqlQuery<ColunaDoIndice>($@"
        SELECT c.name AS Nome, CAST(ic.key_ordinal AS INT) AS Ordem, ic.is_descending_key AS Descendente
        FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = OBJECT_ID('dbo.Embalagem') AND i.name = 'IX_Embalagem_EstruturaItem'
        ORDER BY ic.key_ordinal").ToListAsync();

    Assert.Collection(colunas,
        c => { Assert.Equal("EstruturaItemId", c.Nome); Assert.False(c.Descendente); },
        c => { Assert.Equal("Id", c.Nome); Assert.True(c.Descendente); });
  }

  public sealed class ColunaDoIndice
  {
    public string Nome { get; set; } = string.Empty;
    public int Ordem { get; set; }
    public bool Descendente { get; set; }
  }
}

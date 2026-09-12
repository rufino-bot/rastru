using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Infrastructure.Tests.Persistence;

// A string vem da CONSTANTE, nao de `nameof`: o `[CollectionDefinition]` da classe usa
// `ColecaoQueEscreveEmComponente.Nome` ("escritores de dbo.Componente"), e `nameof` produziria
// outra string. O atributo compilaria, o teste passaria, e a serializacao NAO aconteceria —
// falha silenciosa. (Corrigido em 2026-09-12: o plano trazia `nameof` aqui, medido errado.)
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class ArquivoDeComponenteMapeamentoTests : TesteComBanco
{
  private static byte[] ConteudoDeTeste() => [0x01, 0x02, 0x03, 0x04];

  private static ArquivoDeComponente NovoArquivo(int usuarioId) =>
      new()
      {
        NomeOriginal = "peca-de-teste.stl",
        Conteudo = ConteudoDeTeste(),
        TamanhoEmBytes = ConteudoDeTeste().Length,
        Sha256 = SHA256.HashData(ConteudoDeTeste()),
        CriadoPorUsuarioId = usuarioId,
      };

  [Fact]
  public async Task Arquivo_grava_e_le_o_blob_de_volta_byte_a_byte()
  {
    await using var db = NovoContexto();
    var usuarioId = await db.Usuarios.Select(u => u.Id).FirstAsync();

    var arquivo = NovoArquivo(usuarioId);
    db.Set<ArquivoDeComponente>().Add(arquivo);
    await db.SaveChangesAsync();

    db.ChangeTracker.Clear();
    var lido = await db.Set<ArquivoDeComponente>().AsNoTracking()
        .SingleAsync(a => a.Id == arquivo.Id);

    Assert.Equal(ConteudoDeTeste(), lido.Conteudo);
    Assert.Equal("peca-de-teste.stl", lido.NomeOriginal);
    Assert.Equal(32, lido.Sha256.Length);
    // CriadoEm vem do DEFAULT do banco (Database First), nao do C#: se o mapeamento tentar
    // gravar o default do DateTime, isto vira 0001-01-01.
    Assert.True(lido.CriadoEm > new DateTime(2020, 1, 1));

    db.Set<ArquivoDeComponente>().Remove(lido);
    await db.SaveChangesAsync();
  }

  [Fact]
  public async Task Componente_guarda_o_ArquivoSolidoId_e_a_listagem_nao_traz_o_blob()
  {
    await using var db = NovoContexto();
    var usuarioId = await db.Usuarios.Select(u => u.Id).FirstAsync();

    var arquivo = NovoArquivo(usuarioId);
    db.Set<ArquivoDeComponente>().Add(arquivo);
    await db.SaveChangesAsync();

    var componente = new Componente
    {
      Codigo = $"ARQ-{Guid.NewGuid():N}"[..12],
      Descricao = "Componente do teste de arquivo",
      Tipo = "Fabricado",
      Ativo = true,
      ArquivoSolidoId = arquivo.Id,
    };
    db.Componentes.Add(componente);
    await db.SaveChangesAsync();

    db.ChangeTracker.Clear();
    // Escopado pelo PROPRIO id, nao por contagem global da tabela: Api.Tests escreve em
    // dbo.Componente em outro processo, e [Collection] nao atravessa processo.
    var lido = await db.Componentes.AsNoTracking().SingleAsync(c => c.Id == componente.Id);
    Assert.Equal(arquivo.Id, lido.ArquivoSolidoId);

    // A prova de que o blob nao vem junto e de DESENHO: `Componente` nao tem propriedade de
    // navegacao para `ArquivoDeComponente`, entao nao existe `Include` a escrever aqui. Se alguem
    // acrescentar a navegacao, esta linha para de compilar e o desenho volta a ser discutido.
    Assert.Empty(
        db.Model.FindEntityType(typeof(Componente))!.GetNavigations());

    db.Componentes.Remove(lido);
    await db.SaveChangesAsync();
    db.Set<ArquivoDeComponente>().Remove(
        await db.Set<ArquivoDeComponente>().SingleAsync(a => a.Id == arquivo.Id));
    await db.SaveChangesAsync();
  }
}

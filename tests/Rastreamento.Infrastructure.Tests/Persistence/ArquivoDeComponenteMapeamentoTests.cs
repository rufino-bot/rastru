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

  // TamanhoEmBytes e Sha256 NAO entram aqui: sao colunas calculadas PERSISTED pelo banco desde a
  // emenda de 2026-09-12 (ver XML doc de ArquivoDeComponente). O SQL Server recusa escrita nelas
  // (Msg 271) -- o valor que o C# atribuisse aqui seria descartado de qualquer jeito.
  private static ArquivoDeComponente NovoArquivo(int usuarioId) =>
      new()
      {
        NomeOriginal = "peca-de-teste.stl",
        Conteudo = ConteudoDeTeste(),
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

    try
    {
      db.ChangeTracker.Clear();
      var lido = await db.Set<ArquivoDeComponente>().AsNoTracking()
          .SingleAsync(a => a.Id == arquivo.Id);

      Assert.Equal(ConteudoDeTeste(), lido.Conteudo);
      Assert.Equal("peca-de-teste.stl", lido.NomeOriginal);
      // Sha256 e TamanhoEmBytes sao CALCULADOS pelo banco (colunas PERSISTED sobre Conteudo):
      // comparar contra o hash calculado em C# prova a coluna computada de verdade, nao so o
      // comprimento -- `Assert.Equal(32, lido.Sha256.Length)` passaria igual com os bytes
      // trocados.
      Assert.Equal(SHA256.HashData(ConteudoDeTeste()), lido.Sha256);
      Assert.Equal(ConteudoDeTeste().Length, lido.TamanhoEmBytes);
      Assert.Equal(usuarioId, lido.CriadoPorUsuarioId);
      // CriadoEm vem do DEFAULT do banco (Database First), nao do C#: se o mapeamento tentar
      // gravar o default do DateTime, isto vira 0001-01-01.
      Assert.True(lido.CriadoEm > new DateTime(2020, 1, 1));
    }
    finally
    {
      await using var dbLimpeza = NovoContexto();
      dbLimpeza.Set<ArquivoDeComponente>().Remove(
          await dbLimpeza.Set<ArquivoDeComponente>().SingleAsync(a => a.Id == arquivo.Id));
      await dbLimpeza.SaveChangesAsync();
    }
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

    try
    {
      db.ChangeTracker.Clear();
      // Escopado pelo PROPRIO id, nao por contagem global da tabela: Api.Tests escreve em
      // dbo.Componente em outro processo, e [Collection] nao atravessa processo.
      var lido = await db.Componentes.AsNoTracking().SingleAsync(c => c.Id == componente.Id);
      Assert.Equal(arquivo.Id, lido.ArquivoSolidoId);

      // A prova de que o blob nao vem junto e de DESENHO: `Componente` nao tem propriedade de
      // navegacao para `ArquivoDeComponente`, entao nao existe `Include` a escrever aqui. A
      // guarda interroga o MODELO do EF em tempo de EXECUCAO -- acrescentar
      // `public ArquivoDeComponente? ArquivoSolido { get; set; }` em Componente COMPILA
      // normalmente, e so este Assert, rodado, discorda (medido por mutacao em 2026-09-12: a falha
      // e `Assert... Failure`, nao erro do compilador). Afirma sobre o
      // TargetEntityType de cada navegacao, nao sobre a colecao estar vazia: uma navegacao
      // legitima futura que NAO aponte para ArquivoDeComponente nao deveria reprovar aqui.
      Assert.DoesNotContain(
          db.Model.FindEntityType(typeof(Componente))!.GetNavigations(),
          n => n.TargetEntityType.ClrType == typeof(ArquivoDeComponente));
    }
    finally
    {
      await using var dbLimpeza = NovoContexto();
      dbLimpeza.Componentes.Remove(
          await dbLimpeza.Componentes.SingleAsync(c => c.Id == componente.Id));
      await dbLimpeza.SaveChangesAsync();
      dbLimpeza.Set<ArquivoDeComponente>().Remove(
          await dbLimpeza.Set<ArquivoDeComponente>().SingleAsync(a => a.Id == arquivo.Id));
      await dbLimpeza.SaveChangesAsync();
    }
  }
}

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// O <c>ArquivoDeComponenteRepository</c> contra o SQL Server REAL. Os dois <c>[Fact]</c> de
/// <see cref="ArquivoDeComponenteMapeamentoTests"/> provam o MAPEAMENTO (o EF grava e le os campos
/// certos); nenhum deles chamava o repositorio -- o que este arquivo fecha e o REPOSITORIO em si:
/// a transacao que liga arquivo a Componente, o colapso null de "componente inexistente" e "sem
/// solido", a substituicao (historico, nao remocao) e a projecao de metadado que nunca traz o
/// blob.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class ArquivoDeComponenteRepositoryTests : TesteComBanco
{
  private static byte[] ConteudoDeTeste(byte marcador = 0x01) => [marcador, 0x02, 0x03, 0x04];

  // TamanhoEmBytes e Sha256 NAO entram aqui: sao colunas calculadas PERSISTED pelo banco.
  private static ArquivoDeComponente NovoArquivo(int usuarioId, string nome, byte[] conteudo) =>
      new() { NomeOriginal = nome, Conteudo = conteudo, CriadoPorUsuarioId = usuarioId };

  private static async Task<int> NovoComponenteAsync(RastreamentoDbContext db)
  {
    var componente = new Componente
    {
      Codigo = $"ARQ-{Guid.NewGuid():N}"[..12],
      Descricao = "Componente do teste de repositorio",
      Tipo = "Fabricado",
      Ativo = true,
    };
    db.Componentes.Add(componente);
    await db.SaveChangesAsync();
    return componente.Id;
  }

  /// <summary>
  /// Um id que com CERTEZA nao existe: cria a linha e apaga na hora. IDENTITY nao reaproveita
  /// valor, entao o id fica livre para sempre -- mais confiavel que chutar um numero alto, que o
  /// banco de dev compartilhado pode ja ter alcancado. Mesmo padrao de
  /// <c>ReceitaPadraoRepositoryTests.IdDeComponenteQueNaoExiste</c>.
  /// </summary>
  private static async Task<int> IdDeComponenteQueNaoExisteAsync()
  {
    await using var db = NovoContexto();
    var id = await NovoComponenteAsync(db);
    db.Componentes.Remove(await db.Componentes.SingleAsync(c => c.Id == id));
    await db.SaveChangesAsync();
    return id;
  }

  /// <summary>
  /// Duas chamadas a SaveChanges, NUNCA uma so: sem navegacao entre Componente e
  /// ArquivoDeComponente (por desenho), o EF nao enxerga FK_Componente_ArquivoSolido no modelo e
  /// nao sabe ordenar os deletes -- medido: um SaveChanges so, com os dois Remove juntos, viola a
  /// FK em 3 de 3 execucoes, porque o EF escolhe apagar o ArquivoDeComponente antes do Componente
  /// que aponta para ele. Nao e intermitencia nem concorrencia: os dois Remove iriam no MESMO
  /// SaveChanges, numa thread so, e a ordem que o EF escolhe sem o metadado da FK e
  /// deterministica para este modelo -- deterministica e errada. Apagar o Componente (quem tem a
  /// FK) primeiro, e so depois o arquivo, deixa de depender dessa ordem.
  /// </summary>
  private static async Task LimparAsync(int componenteId, params int[] arquivoIds)
  {
    await using var db = NovoContexto();
    var componente = await db.Componentes.SingleOrDefaultAsync(c => c.Id == componenteId);
    if (componente is not null)
    {
      db.Componentes.Remove(componente);
      await db.SaveChangesAsync();
    }

    db.ArquivosDeComponente.RemoveRange(
        await db.ArquivosDeComponente.Where(a => arquivoIds.Contains(a.Id)).ToListAsync());
    await db.SaveChangesAsync();
  }

  [Fact]
  public async Task GravarEVincularComoSolidoAsync_grava_e_ObterSolidoDoComponenteAsync_le_de_volta_byte_a_byte()
  {
    int usuarioId, componenteId;
    await using (var setup = NovoContexto())
    {
      usuarioId = await setup.Usuarios.Select(u => u.Id).FirstAsync();
      componenteId = await NovoComponenteAsync(setup);
    }

    var conteudo = ConteudoDeTeste();
    int? arquivoId;
    await using (var db = NovoContexto())
    {
      var repo = new ArquivoDeComponenteRepository(db);
      arquivoId = await repo.GravarEVincularComoSolidoAsync(
          componenteId, NovoArquivo(usuarioId, "peca.stl", conteudo), null, CancellationToken.None);
    }

    try
    {
      Assert.NotNull(arquivoId);

      await using var dbLeitura = NovoContexto();
      var repoLeitura = new ArquivoDeComponenteRepository(dbLeitura);
      var lido = await repoLeitura.ObterSolidoDoComponenteAsync(componenteId, CancellationToken.None);

      Assert.NotNull(lido);
      Assert.Equal(conteudo, lido!.Conteudo);
      Assert.Equal("peca.stl", lido.NomeOriginal);
      // Prova a coluna calculada de verdade, nao so o comprimento.
      Assert.Equal(SHA256.HashData(conteudo), lido.Sha256);
      Assert.Equal(conteudo.Length, lido.TamanhoEmBytes);
      Assert.Equal(usuarioId, lido.CriadoPorUsuarioId);

      // O VINCULO em si: Componente.ArquivoSolidoId aponta para o arquivo gravado.
      var componenteVinculado = await dbLeitura.Componentes.AsNoTracking()
          .SingleAsync(c => c.Id == componenteId);
      Assert.Equal(arquivoId, componenteVinculado.ArquivoSolidoId);
    }
    finally
    {
      await LimparAsync(componenteId, arquivoId ?? 0);
    }
  }

  [Fact]
  public async Task ObterSolidoDoComponenteAsync_devolve_null_para_componente_sem_solido()
  {
    int componenteId;
    await using (var db = NovoContexto()) componenteId = await NovoComponenteAsync(db);

    try
    {
      await using var db = NovoContexto();
      var repo = new ArquivoDeComponenteRepository(db);

      var resultado = await repo.ObterSolidoDoComponenteAsync(componenteId, CancellationToken.None);

      Assert.Null(resultado);
    }
    finally
    {
      await LimparAsync(componenteId);
    }
  }

  [Fact]
  public async Task ObterSolidoDoComponenteAsync_devolve_null_para_id_inexistente()
  {
    var idInexistente = await IdDeComponenteQueNaoExisteAsync();

    await using var db = NovoContexto();
    var repo = new ArquivoDeComponenteRepository(db);

    var resultado = await repo.ObterSolidoDoComponenteAsync(idInexistente, CancellationToken.None);

    Assert.Null(resultado);
  }

  [Fact]
  public async Task GravarEVincularComoSolidoAsync_devolve_null_para_componente_inexistente_e_nao_grava_nada()
  {
    var idInexistente = await IdDeComponenteQueNaoExisteAsync();
    int usuarioId;
    await using (var setup = NovoContexto())
      usuarioId = await setup.Usuarios.Select(u => u.Id).FirstAsync();

    var nomeMarcador = $"orfao-{Guid.NewGuid():N}.stl";
    int? resultado;
    await using (var db = NovoContexto())
    {
      var repo = new ArquivoDeComponenteRepository(db);
      resultado = await repo.GravarEVincularComoSolidoAsync(
          idInexistente,
          NovoArquivo(usuarioId, nomeMarcador, ConteudoDeTeste()),
          null, CancellationToken.None);
    }

    Assert.Null(resultado);

    // Prova que NADA foi gravado: a checagem do componente vem antes do primeiro SaveChanges,
    // entao nenhum ArquivoDeComponente com este nome-marcador deveria existir.
    await using var dbConfere = NovoContexto();
    Assert.False(await dbConfere.ArquivosDeComponente.AnyAsync(a => a.NomeOriginal == nomeMarcador));
  }

  [Fact]
  public async Task Substituicao_aponta_para_o_segundo_arquivo_e_mantem_o_primeiro_na_tabela()
  {
    int usuarioId, componenteId;
    await using (var setup = NovoContexto())
    {
      usuarioId = await setup.Usuarios.Select(u => u.Id).FirstAsync();
      componenteId = await NovoComponenteAsync(setup);
    }

    int? primeiroId, segundoId;
    await using (var db = NovoContexto())
    {
      var repo = new ArquivoDeComponenteRepository(db);
      primeiroId = await repo.GravarEVincularComoSolidoAsync(
          componenteId,
          NovoArquivo(usuarioId, "v1.stl", ConteudoDeTeste(0x01)),
          null, CancellationToken.None);
    }
    await using (var db = NovoContexto())
    {
      var repo = new ArquivoDeComponenteRepository(db);
      segundoId = await repo.GravarEVincularComoSolidoAsync(
          componenteId,
          NovoArquivo(usuarioId, "v2.stl", ConteudoDeTeste(0x09)),
          null, CancellationToken.None);
    }

    try
    {
      Assert.NotNull(primeiroId);
      Assert.NotNull(segundoId);
      Assert.NotEqual(primeiroId, segundoId);

      await using var dbLeitura = NovoContexto();
      var componente = await dbLeitura.Componentes.AsNoTracking()
          .SingleAsync(c => c.Id == componenteId);
      Assert.Equal(segundoId, componente.ArquivoSolidoId);

      // O primeiro PERMANECE na tabela: nao existe remocao de solido (§2.5 da spec) -- vira
      // historico, nao lixo coletado.
      Assert.True(await dbLeitura.ArquivosDeComponente.AnyAsync(a => a.Id == primeiroId!.Value));
    }
    finally
    {
      await LimparAsync(componenteId, primeiroId ?? 0, segundoId ?? 0);
    }
  }

  [Fact]
  public async Task ObterMetadadoDoSolidoAsync_devolve_nome_e_tamanho_sem_o_blob()
  {
    int usuarioId, componenteId;
    await using (var setup = NovoContexto())
    {
      usuarioId = await setup.Usuarios.Select(u => u.Id).FirstAsync();
      componenteId = await NovoComponenteAsync(setup);
    }

    var conteudo = ConteudoDeTeste();
    int? arquivoId;
    await using (var db = NovoContexto())
    {
      var repo = new ArquivoDeComponenteRepository(db);
      arquivoId = await repo.GravarEVincularComoSolidoAsync(
          componenteId, NovoArquivo(usuarioId, "metadado.stl", conteudo), null, CancellationToken.None);
    }

    try
    {
      await using var dbLeitura = NovoContexto();
      var repo = new ArquivoDeComponenteRepository(dbLeitura);

      var metadado = await repo.ObterMetadadoDoSolidoAsync(componenteId, CancellationToken.None);

      Assert.NotNull(metadado);
      Assert.Equal("metadado.stl", metadado!.NomeOriginal);
      Assert.Equal(conteudo.Length, metadado.TamanhoEmBytes);
      // A garantia de que o blob NAO vem e de TIPO, nao de execucao: MetadadoDeSolido nao tem
      // propriedade Conteudo, entao nao ha campo aqui para afirmar "esta vazio" -- so a ausencia
      // do proprio campo. Trocar a projecao por materializar ArquivoDeComponente inteiro (medido
      // por mutacao) ainda compila e ainda passa neste teste: essa mutacao mede se a
      // garantia e estrutural (que e), nao se este teste a fiscaliza em runtime.
    }
    finally
    {
      await LimparAsync(componenteId, arquivoId ?? 0);
    }
  }

  [Fact]
  public async Task ObterMetadadoDoSolidoAsync_devolve_null_para_componente_sem_solido_e_para_id_inexistente()
  {
    int componenteId;
    await using (var db = NovoContexto()) componenteId = await NovoComponenteAsync(db);
    var idInexistente = await IdDeComponenteQueNaoExisteAsync();

    try
    {
      await using var db = NovoContexto();
      var repo = new ArquivoDeComponenteRepository(db);

      Assert.Null(await repo.ObterMetadadoDoSolidoAsync(componenteId, CancellationToken.None));
      Assert.Null(await repo.ObterMetadadoDoSolidoAsync(idInexistente, CancellationToken.None));
    }
    finally
    {
      await LimparAsync(componenteId);
    }
  }

  // ---------------------------------------------------------------- MINIATURA

  /// <summary>
  /// O repositorio nao valida PNG (quem valida e o caso de uso, com o <c>ValidadorDeMiniatura</c>):
  /// aqui bastam bytes distintos dos do solido, para uma FK trocada pela outra nao passar.
  /// </summary>
  private static byte[] MiniaturaDeTeste(byte marcador = 0xA1) => [0x89, 0x50, 0x4E, 0x47, marcador];

  private static async Task<int[]> IdsDeArquivoDoUsuarioComNomeAsync(string nome)
  {
    await using var db = NovoContexto();
    return await db.ArquivosDeComponente.AsNoTracking()
        .Where(a => a.NomeOriginal == nome).Select(a => a.Id).ToArrayAsync();
  }

  [Fact]
  public async Task Gravar_solido_com_miniatura_liga_as_duas_FKs_na_mesma_transacao()
  {
    int usuarioId, componenteId;
    await using (var setup = NovoContexto())
    {
      usuarioId = await setup.Usuarios.Select(u => u.Id).FirstAsync();
      componenteId = await NovoComponenteAsync(setup);
    }

    var solido = NovoArquivo(usuarioId, "peca.stl", ConteudoDeTeste());
    var miniatura = NovoArquivo(usuarioId, "miniatura.png", MiniaturaDeTeste());
    int? solidoId;
    await using (var db = NovoContexto())
    {
      solidoId = await new ArquivoDeComponenteRepository(db).GravarEVincularComoSolidoAsync(
          componenteId, solido, miniatura, CancellationToken.None);
    }

    try
    {
      Assert.Equal(solido.Id, solidoId);
      Assert.NotEqual(0, miniatura.Id);
      Assert.NotEqual(solido.Id, miniatura.Id);

      await using var dbLeitura = NovoContexto();
      var componente = await dbLeitura.Componentes.AsNoTracking().SingleAsync(c => c.Id == componenteId);
      Assert.Equal(solido.Id, componente.ArquivoSolidoId);
      Assert.Equal(miniatura.Id, componente.ArquivoMiniaturaId);

      var repo = new ArquivoDeComponenteRepository(dbLeitura);
      var lida = await repo.ObterMiniaturaDoComponenteAsync(componenteId, CancellationToken.None);
      Assert.NotNull(lida);
      Assert.Equal(MiniaturaDeTeste(), lida!.Conteudo);
      Assert.Equal("miniatura.png", lida.NomeOriginal);
      // A versao que a API expoe sai desta coluna calculada.
      Assert.Equal(SHA256.HashData(MiniaturaDeTeste()), lida.Sha256);

      // E o solido continua sendo o STL: as duas leituras nao se cruzam.
      var solidoLido = await repo.ObterSolidoDoComponenteAsync(componenteId, CancellationToken.None);
      Assert.Equal(ConteudoDeTeste(), solidoLido!.Conteudo);
    }
    finally
    {
      await LimparAsync(componenteId, solido.Id, miniatura.Id);
    }
  }

  /// <summary>
  /// "Na mesma transacao" pelo lado da falha: a miniatura aponta um usuario que nao existe, o
  /// INSERT dela viola <c>FK_ArquivoDeComponente_CriadoPorUsuario</c>, e nem o solido fica gravado
  /// nem o Componente muda. Escopado pelo NOME unico do solido, nunca por contagem da tabela.
  /// </summary>
  [Fact]
  public async Task Miniatura_que_falha_ao_gravar_desfaz_tambem_o_solido()
  {
    int usuarioId, componenteId;
    await using (var setup = NovoContexto())
    {
      usuarioId = await setup.Usuarios.Select(u => u.Id).FirstAsync();
      componenteId = await NovoComponenteAsync(setup);
    }
    var nomeDoSolido = $"falha-{Guid.NewGuid():N}.stl";

    try
    {
      await using (var db = NovoContexto())
      {
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            new ArquivoDeComponenteRepository(db).GravarEVincularComoSolidoAsync(
                componenteId,
                NovoArquivo(usuarioId, nomeDoSolido, ConteudoDeTeste()),
                NovoArquivo(int.MaxValue, "miniatura.png", MiniaturaDeTeste()),
                CancellationToken.None));
      }

      Assert.Empty(await IdsDeArquivoDoUsuarioComNomeAsync(nomeDoSolido));
      await using var dbLeitura = NovoContexto();
      var componente = await dbLeitura.Componentes.AsNoTracking().SingleAsync(c => c.Id == componenteId);
      Assert.Null(componente.ArquivoSolidoId);
      Assert.Null(componente.ArquivoMiniaturaId);
    }
    finally
    {
      await LimparAsync(componenteId, await IdsDeArquivoDoUsuarioComNomeAsync(nomeDoSolido));
    }
  }

  [Fact]
  public async Task Gravar_solido_sem_miniatura_zera_a_FK_e_mantem_a_miniatura_anterior_na_tabela()
  {
    int usuarioId, componenteId;
    await using (var setup = NovoContexto())
    {
      usuarioId = await setup.Usuarios.Select(u => u.Id).FirstAsync();
      componenteId = await NovoComponenteAsync(setup);
    }

    var primeiro = NovoArquivo(usuarioId, "v1.stl", ConteudoDeTeste(0x01));
    var miniatura = NovoArquivo(usuarioId, "miniatura.png", MiniaturaDeTeste());
    var segundo = NovoArquivo(usuarioId, "v2.stl", ConteudoDeTeste(0x09));
    await using (var db = NovoContexto())
    {
      await new ArquivoDeComponenteRepository(db).GravarEVincularComoSolidoAsync(
          componenteId, primeiro, miniatura, CancellationToken.None);
    }
    await using (var db = NovoContexto())
    {
      await new ArquivoDeComponenteRepository(db).GravarEVincularComoSolidoAsync(
          componenteId, segundo, null, CancellationToken.None);
    }

    try
    {
      await using var dbLeitura = NovoContexto();
      var componente = await dbLeitura.Componentes.AsNoTracking().SingleAsync(c => c.Id == componenteId);
      Assert.Equal(segundo.Id, componente.ArquivoSolidoId);
      Assert.Null(componente.ArquivoMiniaturaId);
      Assert.Null(await new ArquivoDeComponenteRepository(dbLeitura)
          .ObterMiniaturaDoComponenteAsync(componenteId, CancellationToken.None));

      // A miniatura anterior PERMANECE na tabela, como o solido anterior (C4 da spec da miniatura).
      Assert.True(await dbLeitura.ArquivosDeComponente.AnyAsync(a => a.Id == miniatura.Id));
    }
    finally
    {
      await LimparAsync(componenteId, primeiro.Id, miniatura.Id, segundo.Id);
    }
  }

  [Fact]
  public async Task ObterMiniaturaDoComponenteAsync_devolve_null_para_componente_inexistente()
  {
    var idInexistente = await IdDeComponenteQueNaoExisteAsync();

    await using var db = NovoContexto();
    Assert.Null(await new ArquivoDeComponenteRepository(db)
        .ObterMiniaturaDoComponenteAsync(idInexistente, CancellationToken.None));
  }
}

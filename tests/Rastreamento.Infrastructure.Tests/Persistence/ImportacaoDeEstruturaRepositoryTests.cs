using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;
using static Rastreamento.Infrastructure.Tests.Persistence.RascunhoDeTesteNoBanco;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// `ImportacaoDeEstruturaRepository` e `ComponenteRepository.ListarPorCodigosAsync` contra o SQL
/// Server real. Cada teste cria a sua `ArvoreDeTesteNoBanco` e afirma so sobre as linhas dela.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class ImportacaoDeEstruturaRepositoryTests : TesteComBanco
{
  private async Task NaArvoreAsync(Func<ArvoreDeTesteNoBanco, Task> corpo)
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "impr");
    try { await corpo(arvore); }
    finally
    {
      await LimparAsync(NovoContexto, arvore.AgrupamentoId);
      await arvore.LimparAsync(NovoContexto);
    }
  }

  private async Task AdicionarAsync(ImportacaoDeEstrutura importacao)
  {
    await using var db = NovoContexto();
    await new ImportacaoDeEstruturaRepository(db).AdicionarAsync(importacao, CancellationToken.None);
  }

  [Fact]
  public Task ExcluirAsync_apaga_rascunho_registros_filhos_e_arquivos_pendentes() => NaArvoreAsync(async a =>
  {
    int arquivoId;
    await using (var preparo = NovoContexto())
    {
      arquivoId = await new ImportacaoDeEstruturaRepository(preparo).GravarArquivoPendenteAsync(
          new ArquivoDeComponente { NomeOriginal = "pendente.stl", Conteudo = [1, 2, 3], CriadoPorUsuarioId = a.AutorId },
          CancellationToken.None);
    }
    var raiz = Registro(null, "Raiz");
    var comSolido = Registro($"{a.AgrupamentoId}-STL", "Com solido pendente");
    comSolido.ArquivoSolidoPendenteId = arquivoId;
    var outro = Registro($"{a.AgrupamentoId}-OUT", "Outro");
    Aresta(raiz, comSolido, 1m);
    Aresta(raiz, outro, 3m);
    Aresta(comSolido, outro, 2m);
    var importacao = Montar(a, "excluir.xlsx", raiz, comSolido, outro);
    await AdicionarAsync(importacao);
    var registroIds = importacao.Componentes.Select(c => c.Id).ToArray();
    var filhoIds = importacao.Componentes.SelectMany(c => c.Filhos).Select(f => f.Id).ToArray();
    Assert.Equal(3, registroIds.Length);
    Assert.Equal(3, filhoIds.Length);

    await using (var exclusao = NovoContexto())
      await new ImportacaoDeEstruturaRepository(exclusao).ExcluirAsync(importacao.Id, CancellationToken.None);

    await using var db = NovoContexto();
    Assert.Equal(0, await db.ImportacoesDeEstrutura.CountAsync(i => i.Id == importacao.Id));
    Assert.Equal(0, await db.ImportacoesDeEstruturaComponentes.CountAsync(c => registroIds.Contains(c.Id)));
    Assert.Equal(0, await db.ImportacoesDeEstruturaFilhos.CountAsync(f => filhoIds.Contains(f.Id)));
    Assert.Equal(0, await db.ArquivosDeComponente.CountAsync(x => x.Id == arquivoId));
  });

  [Fact]
  public Task SalvarAsync_com_versao_velha_lanca_ConflitoDeConcorrenciaException() => NaArvoreAsync(async a =>
  {
    var importacao = Montar(a, "conflito.xlsx", Registro(null, "Raiz"));
    await AdicionarAsync(importacao);

    await using var primeiro = NovoContexto();
    await using var segundo = NovoContexto();
    var repoPrimeiro = new ImportacaoDeEstruturaRepository(primeiro);
    var repoSegundo = new ImportacaoDeEstruturaRepository(segundo);
    var doPrimeiro = (await repoPrimeiro.ObterAsync(importacao.Id, CancellationToken.None))!;
    var doSegundo = (await repoSegundo.ObterAsync(importacao.Id, CancellationToken.None))!;
    var versaoLida = doPrimeiro.Versao.ToArray();

    doSegundo.QuantidadeDaPeca = 7m;
    await repoSegundo.SalvarAsync(doSegundo, doSegundo.Versao.ToArray(), CancellationToken.None);

    doPrimeiro.QuantidadeDaPeca = 9m;
    await Assert.ThrowsAsync<ConflitoDeConcorrenciaException>(
        () => repoPrimeiro.SalvarAsync(doPrimeiro, versaoLida, CancellationToken.None));

    await using var leitura = NovoContexto();
    var gravada = await leitura.ImportacoesDeEstrutura.AsNoTracking().SingleAsync(i => i.Id == importacao.Id);
    Assert.Equal(7m, gravada.QuantidadeDaPeca);
  });

  [Fact]
  public Task ListarDoAgrupamentoAsync_traz_nome_do_autor_e_ordena_do_mais_recente() => NaArvoreAsync(async a =>
  {
    var antiga = Montar(a, "antiga.xlsx", Registro(null, "Raiz antiga"));
    antiga.CriadoEm = DateTime.UtcNow.AddHours(-2);
    var recente = Montar(a, "recente.xlsx", Registro(null, "Raiz recente"));
    recente.CriadoEm = DateTime.UtcNow.AddHours(-1);
    await AdicionarAsync(antiga);
    await AdicionarAsync(recente);

    await using var db = NovoContexto();
    var nomeDoAutor = await db.Usuarios.AsNoTracking().Where(u => u.Id == a.AutorId).Select(u => u.NomeCompleto).SingleAsync();
    var repo = new ImportacaoDeEstruturaRepository(db);

    var lista = await repo.ListarDoAgrupamentoAsync(a.AgrupamentoId, CancellationToken.None);

    Assert.Equal(["recente.xlsx", "antiga.xlsx"], lista.Select(r => r.NomeDoArquivo));
    Assert.Equal([recente.Id, antiga.Id], lista.Select(r => r.Id));
    Assert.All(lista, r => Assert.Equal(nomeDoAutor, r.CriadoPor));
    Assert.Equal(await repo.ObterNomeDoAutorAsync(a.AutorId, CancellationToken.None), nomeDoAutor);
    Assert.True(lista[0].CriadoEm > lista[1].CriadoEm);
    Assert.True(lista[0].AtualizadoEm >= lista[0].CriadoEm.AddSeconds(-1));
  });

  [Fact]
  public Task ObterMetadadosAsync_devolve_nome_e_tamanho_sem_carregar_o_conteudo() => NaArvoreAsync(async a =>
  {
    await using var db = NovoContexto();
    var repo = new ImportacaoDeEstruturaRepository(db);
    var id = await repo.GravarArquivoPendenteAsync(
        new ArquivoDeComponente { NomeOriginal = "metadado.stl", Conteudo = new byte[1234], CriadoPorUsuarioId = a.AutorId },
        CancellationToken.None);
    try
    {
      var metadados = await repo.ObterMetadadosAsync([id, int.MaxValue], CancellationToken.None);

      var unico = Assert.Single(metadados);
      Assert.Equal(id, unico.Key);
      Assert.Equal(new MetadadoDeSolido("metadado.stl", 1234), unico.Value);
      Assert.Empty(await repo.ObterMetadadosAsync([], CancellationToken.None));

      var arquivo = await repo.ObterArquivoAsync(id, CancellationToken.None);
      Assert.Equal(1234, arquivo!.Conteudo.Length);
      Assert.Null(await repo.ObterArquivoAsync(int.MaxValue, CancellationToken.None));
    }
    finally
    {
      await repo.ExcluirArquivosAsync([id], CancellationToken.None);
    }
    await using var depois = NovoContexto();
    Assert.Equal(0, await depois.ArquivosDeComponente.CountAsync(x => x.Id == id));
  });

  [Fact]
  public Task ExisteNoAgrupamentoAsync_so_e_verdadeiro_com_rascunho() => NaArvoreAsync(async a =>
  {
    await using var db = NovoContexto();
    var repo = new ImportacaoDeEstruturaRepository(db);
    Assert.False(await repo.ExisteNoAgrupamentoAsync(a.AgrupamentoId, CancellationToken.None));

    await AdicionarAsync(Montar(a, "existe.xlsx", Registro(null, "Raiz")));

    Assert.True(await repo.ExisteNoAgrupamentoAsync(a.AgrupamentoId, CancellationToken.None));
  });

  [Fact]
  public async Task ListarPorCodigosAsync_ignora_caixa_e_traz_inativo()
  {
    var sufixo = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    var ativo = new Componente { Codigo = $"LPC-A-{sufixo}", Descricao = "Ativo", Tipo = "Fabricado", Ativo = true };
    var inativo = new Componente { Codigo = $"LPC-I-{sufixo}", Descricao = "Inativo", Tipo = "Bruto", Ativo = false };
    var fora = new Componente { Codigo = $"LPC-F-{sufixo}", Descricao = "Fora da busca", Tipo = "Bruto", Ativo = true };
    await using var db = NovoContexto();
    db.Componentes.AddRange(ativo, inativo, fora);
    await db.SaveChangesAsync();
    try
    {
      var achados = await new ComponenteRepository(db).ListarPorCodigosAsync(
          [ativo.Codigo.ToLowerInvariant(), inativo.Codigo, $"LPC-NAO-EXISTE-{sufixo}"], CancellationToken.None);

      Assert.Equal(new[] { ativo.Id, inativo.Id }.Order(), achados.Select(c => c.Id).Order());
      Assert.False(achados.Single(c => c.Id == inativo.Id).Ativo);
      Assert.Empty(await new ComponenteRepository(db).ListarPorCodigosAsync([], CancellationToken.None));
    }
    finally
    {
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.Componente WHERE Id IN ({ativo.Id}, {inativo.Id}, {fora.Id})");
    }
  }
}

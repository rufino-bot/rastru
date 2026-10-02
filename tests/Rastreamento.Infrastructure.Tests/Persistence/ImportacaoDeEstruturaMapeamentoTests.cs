using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;
using static Rastreamento.Infrastructure.Tests.Persistence.RascunhoDeTesteNoBanco;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// As tres tabelas do rascunho do import do BOM contra o SQL Server real: o mapeamento faz a volta
/// completa e o indice filtrado do codigo recusa so o que deve. Na mesma colecao das demais classes
/// que usam `ArvoreDeTesteNoBanco`, que escreve em `Componente`.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class ImportacaoDeEstruturaMapeamentoTests : TesteComBanco
{
  private async Task NaArvoreAsync(Func<ArvoreDeTesteNoBanco, Task> corpo)
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "imp");
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
  public Task Grava_e_le_o_rascunho_inteiro_com_raiz_registros_e_filhos() => NaArvoreAsync(async a =>
  {
    var raiz = Registro(null, "Suporte do motor");
    var parafuso = Registro($"{a.AgrupamentoId}-PAR", "Parafuso M8");
    var chapa = Registro($"{a.AgrupamentoId}-CHA", "Chapa 3mm");
    parafuso.TipoNovo = "Bruto"; parafuso.CodigoNovo = "NOVO-PAR"; parafuso.DescricaoNova = "Parafuso M8 novo";
    chapa.ComponenteId = a.ComponenteId; chapa.EscolhaDeReceita = "Importada";
    Aresta(raiz, parafuso, 4.5m);
    Aresta(raiz, chapa, 2m);
    var importacao = Montar(a, "suporte.xlsx", raiz, parafuso, chapa);
    importacao.QuantidadeDaPeca = 10m;
    importacao.RequerRelatorioDimensional = true;

    await AdicionarAsync(importacao);

    await using var db = NovoContexto();
    var lido = await db.ImportacoesDeEstrutura.AsNoTracking()
        .Include(i => i.Componentes).ThenInclude(c => c.Filhos)
        .SingleAsync(i => i.Id == importacao.Id);
    Assert.Equal(("suporte.xlsx", a.AgrupamentoId, a.AutorId, 10m, true),
        (lido.NomeDoArquivo, lido.AgrupamentoId, lido.CriadoPorUsuarioId, lido.QuantidadeDaPeca, lido.RequerRelatorioDimensional));
    Assert.Equal(3, lido.Componentes.Count);
    var registroRaiz = lido.Componentes.Single(c => c.CodigoLido is null);
    Assert.Equal(registroRaiz.Id, lido.RaizId);
    Assert.Equal("Suporte do motor", registroRaiz.DescricaoLida);
    Assert.True(lido.CriadoEm > DateTime.UtcNow.AddMinutes(-5));
    Assert.True(lido.AtualizadoEm > DateTime.UtcNow.AddMinutes(-5));
    Assert.Equal(8, lido.Versao.Length);

    var lidoParafuso = lido.Componentes.Single(c => c.CodigoLido == parafuso.CodigoLido);
    Assert.Equal(("Bruto", "NOVO-PAR", "Parafuso M8 novo", (int?)null), (lidoParafuso.TipoNovo, lidoParafuso.CodigoNovo, lidoParafuso.DescricaoNova, lidoParafuso.ComponenteId));
    var lidoChapa = lido.Componentes.Single(c => c.CodigoLido == chapa.CodigoLido);
    Assert.Equal((a.ComponenteId, "Importada"), (lidoChapa.ComponenteId, lidoChapa.EscolhaDeReceita));

    Assert.Equal(2, registroRaiz.Filhos.Count);
    var arestaParafuso = registroRaiz.Filhos.Single(f => f.FilhoId == lidoParafuso.Id);
    Assert.Equal((registroRaiz.Id, 1, 4.5m, 4.5m), (arestaParafuso.PaiId, arestaParafuso.Ordem, arestaParafuso.QuantidadeLida, arestaParafuso.Quantidade));
    var arestaChapa = registroRaiz.Filhos.Single(f => f.FilhoId == lidoChapa.Id);
    Assert.Equal((2, 2m), (arestaChapa.Ordem, arestaChapa.Quantidade));
    Assert.Empty(lidoParafuso.Filhos);
  });

  [Fact]
  public Task Dois_registros_sem_codigo_coexistem_no_mesmo_rascunho() => NaArvoreAsync(async a =>
  {
    var raiz = Registro(null, "Raiz sem codigo");
    var semCodigo = Registro(null, "Item sem part number");
    Aresta(raiz, semCodigo, 1m);

    await AdicionarAsync(Montar(a, "sem-codigo.csv", raiz, semCodigo));

    await using var db = NovoContexto();
    var semPartNumber = await db.ImportacoesDeEstruturaComponentes.AsNoTracking()
        .CountAsync(c => c.ImportacaoId == raiz.ImportacaoId && c.CodigoLido == null);
    Assert.Equal(2, semPartNumber);
  });

  [Fact]
  public Task Codigo_repetido_no_mesmo_rascunho_e_recusado_pelo_indice() => NaArvoreAsync(async a =>
  {
    var raiz = Registro(null, "Raiz");
    var um = Registro($"{a.AgrupamentoId}-DUP", "Primeiro");
    var dois = Registro($"{a.AgrupamentoId}-DUP", "Segundo");

    await using var db = NovoContexto();
    db.ImportacoesDeEstrutura.Add(Montar(a, "dup.xlsx", raiz, um, dois));
    var erro = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

    var sql = Assert.IsType<SqlException>(erro.InnerException);
    Assert.Equal(2601, sql.Number);
    Assert.Contains("UX_ImportacaoDeEstruturaComponente_Codigo", sql.Message);
  });

  [Fact]
  public Task Mesmo_codigo_em_rascunhos_diferentes_e_aceito() => NaArvoreAsync(async a =>
  {
    var codigo = $"{a.AgrupamentoId}-REP";
    var primeiro = Montar(a, "um.xlsx", Registro(null, "Raiz 1"), Registro(codigo, "Repetido"));
    var segundo = Montar(a, "dois.xlsx", Registro(null, "Raiz 2"), Registro(codigo, "Repetido"));

    await AdicionarAsync(primeiro);
    await AdicionarAsync(segundo);

    await using var db = NovoContexto();
    var ids = new[] { primeiro.Id, segundo.Id };
    var registros = await db.ImportacoesDeEstruturaComponentes.AsNoTracking()
        .Where(c => ids.Contains(c.ImportacaoId) && c.CodigoLido == codigo).ToListAsync();
    Assert.Equal(2, registros.Count);
    Assert.Equal(2, registros.Select(r => r.ImportacaoId).Distinct().Count());
  });

  [Fact]
  public Task Versao_muda_quando_so_um_filho_muda_via_SalvarAsync() => NaArvoreAsync(async a =>
  {
    var raiz = Registro(null, "Raiz");
    var filho = Registro($"{a.AgrupamentoId}-FIL", "Filho");
    Aresta(raiz, filho, 2m);
    var importacao = Montar(a, "versao.xlsx", raiz, filho);
    await AdicionarAsync(importacao);

    byte[] antes;
    await using (var escrita = NovoContexto())
    {
      var repo = new ImportacaoDeEstruturaRepository(escrita);
      var rastreada = (await repo.ObterAsync(importacao.Id, CancellationToken.None))!;
      antes = rastreada.Versao.ToArray();
      var atualizadoAntes = rastreada.AtualizadoEm;
      rastreada.Componentes.Single(c => c.CodigoLido is null).Filhos.Single().Quantidade = 5m;

      await repo.SalvarAsync(rastreada, antes, CancellationToken.None);

      Assert.NotEqual(antes, rastreada.Versao);
      Assert.True(rastreada.AtualizadoEm > atualizadoAntes);
    }

    await using var leitura = NovoContexto();
    var lida = await leitura.ImportacoesDeEstrutura.AsNoTracking().SingleAsync(i => i.Id == importacao.Id);
    Assert.NotEqual(antes, lida.Versao);
    var aresta = await leitura.ImportacoesDeEstruturaFilhos.AsNoTracking().SingleAsync(f => f.PaiId == importacao.RaizId);
    Assert.Equal((5m, 2m), (aresta.Quantidade, aresta.QuantidadeLida));
  });
}

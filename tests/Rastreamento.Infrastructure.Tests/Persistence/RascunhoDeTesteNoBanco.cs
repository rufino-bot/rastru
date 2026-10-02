using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Montagem e limpeza de rascunhos de import para os testes de banco. O rascunho nasce com a mesma
/// forma que o caso de uso de importar entrega ao repositorio: a raiz e os registros na colecao
/// `Componentes`, as arestas na colecao `Filhos` do pai, e `Raiz`/`Filho` apontando os objetos (o
/// repositorio resolve os Ids depois do primeiro `SaveChanges`).
/// </summary>
internal static class RascunhoDeTesteNoBanco
{
  public static ImportacaoDeEstruturaComponente Registro(string? codigo, string descricao) =>
      new() { CodigoLido = codigo, DescricaoLida = descricao };

  public static void Aresta(
      ImportacaoDeEstruturaComponente pai, ImportacaoDeEstruturaComponente filho, decimal quantidade) =>
      pai.Filhos.Add(new ImportacaoDeEstruturaFilho
      {
        Filho = filho, Ordem = pai.Filhos.Count + 1, QuantidadeLida = quantidade, Quantidade = quantidade,
      });

  /// <summary>Cabecalho com a raiz (sem codigo) e os registros dados, ainda nao gravado.</summary>
  public static ImportacaoDeEstrutura Montar(
      ArvoreDeTesteNoBanco arvore, string nomeDoArquivo, ImportacaoDeEstruturaComponente raiz,
      params ImportacaoDeEstruturaComponente[] demais) => new()
      {
        AgrupamentoId = arvore.AgrupamentoId,
        NomeDoArquivo = nomeDoArquivo,
        CriadoPorUsuarioId = arvore.AutorId,
        Raiz = raiz,
        Componentes = [raiz, .. demais],
      };

  /// <summary>
  /// Apaga tudo do Agrupamento na ordem das FKs (sem cascata no schema): arestas, `RaizId` nulo,
  /// registros, cabecalhos e, por ultimo, os `ArquivoDeComponente` pendentes. Contexto proprio, para
  /// nao herdar entidade rastreada do teste.
  /// </summary>
  public static async Task LimparAsync(Func<RastreamentoDbContext> novoContexto, int agrupamentoId)
  {
    await using var db = novoContexto();
    var arquivoIds = await db.Database
        .SqlQuery<int>($"""
            SELECT c.ArquivoSolidoPendenteId AS [Value] FROM dbo.ImportacaoDeEstruturaComponente c
            JOIN dbo.ImportacaoDeEstrutura i ON i.Id = c.ImportacaoId
            WHERE i.AgrupamentoId = {agrupamentoId} AND c.ArquivoSolidoPendenteId IS NOT NULL
            """)
        .ToListAsync();
    await db.Database.ExecuteSqlInterpolatedAsync($"""
        DELETE FROM dbo.ImportacaoDeEstruturaFilho WHERE PaiId IN (
          SELECT c.Id FROM dbo.ImportacaoDeEstruturaComponente c
          JOIN dbo.ImportacaoDeEstrutura i ON i.Id = c.ImportacaoId WHERE i.AgrupamentoId = {agrupamentoId})
        """);
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE dbo.ImportacaoDeEstrutura SET RaizId = NULL WHERE AgrupamentoId = {agrupamentoId}");
    await db.Database.ExecuteSqlInterpolatedAsync($"""
        DELETE FROM dbo.ImportacaoDeEstruturaComponente WHERE ImportacaoId IN (
          SELECT Id FROM dbo.ImportacaoDeEstrutura WHERE AgrupamentoId = {agrupamentoId})
        """);
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM dbo.ImportacaoDeEstrutura WHERE AgrupamentoId = {agrupamentoId}");
    foreach (var arquivoId in arquivoIds)
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.ArquivoDeComponente WHERE Id = {arquivoId}");
  }
}

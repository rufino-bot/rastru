using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>Requer o SQL Server no ar (docker compose up -d) com o schema aplicado.</summary>
public class ComponenteMappingTests : TesteComBanco
{
  /// <summary>
  /// Prefixo unico por teste: as consultas de listagem filtram por ele, entao as assercoes de
  /// paginacao nao dependem de a tabela estar vazia nem de outros testes rodando em paralelo.
  /// </summary>
  private static string NovoPrefixo() => $"cmp-{Guid.NewGuid():N}";

  private static Componente Peca(string codigo, string descricao = "Suporte lateral") =>
      new() { Codigo = codigo, Descricao = descricao, Tipo = "Fabricado", Ativo = true };

  private static async Task LimparAsync(string prefixo)
  {
    await using var db = NovoContexto();
    db.Componentes.RemoveRange(
        await db.Componentes.Where(c => c.Codigo.StartsWith(prefixo)).ToListAsync());
    await db.SaveChangesAsync();
  }

  [Fact]
  public async Task Mapeia_componente_com_round_trip()
  {
    var prefixo = NovoPrefixo();
    await using var db = NovoContexto();
    var componente = Peca($"{prefixo}-a", "Suporte lateral esquerdo");

    db.Componentes.Add(componente);
    await db.SaveChangesAsync();
    var id = componente.Id;

    try
    {
      await using var dbLeitura = NovoContexto();
      var carregado = await dbLeitura.Componentes.AsNoTracking().SingleAsync(c => c.Id == id);

      Assert.Equal($"{prefixo}-a", carregado.Codigo);
      Assert.Equal("Suporte lateral esquerdo", carregado.Descricao);
      Assert.Equal("Fabricado", carregado.Tipo);
      Assert.True(carregado.Ativo);
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }

  [Fact]
  public async Task Componente_nasce_ativo_pelo_default_do_banco()
  {
    // INSERT cru omitindo `Ativo` de proposito: e o unico jeito de provar DF_Componente_Ativo,
    // porque um INSERT feito pelo EF sempre manda a coluna (Database First: o default so vive
    // no .sql, ComponenteConfiguration nao declara HasDefaultValue).
    var prefixo = NovoPrefixo();
    await using var db = NovoContexto();
    var codigo = $"{prefixo}-def";

    await db.Database.ExecuteSqlInterpolatedAsync(
        $"INSERT INTO dbo.Componente (Codigo, Descricao, Tipo) VALUES ({codigo}, 'Teste', 'Bruto')");

    try
    {
      await using var dbLeitura = NovoContexto();
      var carregado = await dbLeitura.Componentes.AsNoTracking().SingleAsync(c => c.Codigo == codigo);
      Assert.True(carregado.Ativo);
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }

  [Fact]
  public async Task Pagina_em_ordem_de_codigo_independente_da_ordem_de_insercao()
  {
    // A mutacao que este teste existe para matar: apagar o OrderBy(c => c.Codigo) do repositorio.
    // Sem ordem TOTAL, Skip/Take devolve linhas em ordem arbitraria — a mesma linha pode aparecer
    // em duas paginas e outra em nenhuma. Por isso os codigos sao inseridos FORA de ordem
    // alfabetica: se o repositorio devolvesse na ordem de insercao, a pagina 1 viria "-c", "-a".
    var prefixo = NovoPrefixo();
    await using (var db = NovoContexto())
    {
      db.Componentes.AddRange(Peca($"{prefixo}-c"), Peca($"{prefixo}-a"), Peca($"{prefixo}-b"));
      await db.SaveChangesAsync();
    }

    try
    {
      await using var db = NovoContexto();
      var repo = new ComponenteRepository(db);

      var pagina1 = await repo.ListarAsync(
          new FiltroDeComponente(prefixo, false, 1, 2), CancellationToken.None);
      var pagina2 = await repo.ListarAsync(
          new FiltroDeComponente(prefixo, false, 2, 2), CancellationToken.None);

      Assert.Equal(
          new[] { $"{prefixo}-a", $"{prefixo}-b" }, pagina1.Itens.Select(c => c.Codigo).ToArray());
      Assert.Equal(new[] { $"{prefixo}-c" }, pagina2.Itens.Select(c => c.Codigo).ToArray());
      Assert.Equal(3, pagina1.Total);
      Assert.Equal(3, pagina2.Total);
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }

  [Fact]
  public async Task Busca_casa_no_codigo_e_na_descricao()
  {
    // Mata duas mutacoes: buscar so no Codigo (o caso "descricao" morre) e buscar so na Descricao
    // (o caso "codigo" morre).
    var prefixo = NovoPrefixo();
    // Fatiado para 10 chars: prefixo (36) + "-" (1) + marcador tem que caber em Codigo
    // NVARCHAR(50). Sem a fatia, marcador de 33 chars ($"m{Guid:N}" inteiro) estoura o
    // limite da coluna e o INSERT falha com "String or binary data would be truncated" — achado
    // por mutacao (RED genuino, nao a mutacao proposta pelo Step 9).
    var marcador = $"m{Guid.NewGuid():N}"[..10];
    await using (var db = NovoContexto())
    {
      db.Componentes.AddRange(
          Peca($"{prefixo}-{marcador}", "Sem o marcador na descricao"),
          Peca($"{prefixo}-outro", $"Descricao com {marcador} dentro"),
          Peca($"{prefixo}-terceiro", "Nada a ver"));
      await db.SaveChangesAsync();
    }

    try
    {
      await using var db = NovoContexto();
      var repo = new ComponenteRepository(db);

      var achados = await repo.ListarAsync(
          new FiltroDeComponente(marcador, false, 1, 20), CancellationToken.None);

      Assert.Equal(2, achados.Total);
      Assert.Equal(2, achados.Itens.Count);
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }

  [Fact]
  public async Task Total_respeita_o_filtro_de_inativos()
  {
    // Mata a mutacao "contar o total sem os filtros": com 1 de 3 ativo, o total padrao e 1, nao 3.
    var prefixo = NovoPrefixo();
    await using (var db = NovoContexto())
    {
      db.Componentes.AddRange(
          Peca($"{prefixo}-a"),
          new Componente { Codigo = $"{prefixo}-b", Descricao = "X", Tipo = "Bruto", Ativo = false },
          new Componente { Codigo = $"{prefixo}-c", Descricao = "Y", Tipo = "Bruto", Ativo = false });
      await db.SaveChangesAsync();
    }

    try
    {
      await using var db = NovoContexto();
      var repo = new ComponenteRepository(db);

      var soAtivos = await repo.ListarAsync(
          new FiltroDeComponente(prefixo, false, 1, 20), CancellationToken.None);
      var comInativos = await repo.ListarAsync(
          new FiltroDeComponente(prefixo, true, 1, 20), CancellationToken.None);

      Assert.Equal(1, soAtivos.Total);
      Assert.Equal(3, comInativos.Total);
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }

  [Fact]
  public async Task Pagina_alem_do_fim_devolve_lista_vazia_e_o_total_verdadeiro()
  {
    // Fim de lista nao e erro: e 200 com itens vazios. Tambem mata o off-by-one de trocar
    // Skip((pagina - 1) * tamanho) por Skip(pagina * tamanho), que faria a pagina 1 pular a
    // primeira linha.
    var prefixo = NovoPrefixo();
    await using (var db = NovoContexto())
    {
      db.Componentes.AddRange(Peca($"{prefixo}-a"), Peca($"{prefixo}-b"));
      await db.SaveChangesAsync();
    }

    try
    {
      await using var db = NovoContexto();
      var repo = new ComponenteRepository(db);

      var primeira = await repo.ListarAsync(
          new FiltroDeComponente(prefixo, false, 1, 20), CancellationToken.None);
      var longe = await repo.ListarAsync(
          new FiltroDeComponente(prefixo, false, 99, 20), CancellationToken.None);

      Assert.Equal(
          new[] { $"{prefixo}-a", $"{prefixo}-b" }, primeira.Itens.Select(c => c.Codigo).ToArray());
      Assert.Empty(longe.Itens);
      Assert.Equal(2, longe.Total);
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }

  [Fact]
  public async Task Busca_em_branco_nao_filtra_nada()
  {
    // Prova que `string.IsNullOrWhiteSpace` cobre tambem a string so de espacos: se o repositorio
    // testasse apenas `!= null`, um `busca: "   "` viraria `Contains("   ")` e devolveria zero.
    var prefixo = NovoPrefixo();
    await using (var db = NovoContexto())
    {
      db.Componentes.AddRange(Peca($"{prefixo}-a"), Peca($"{prefixo}-b"));
      await db.SaveChangesAsync();
    }

    try
    {
      await using var db = NovoContexto();
      var repo = new ComponenteRepository(db);

      var todos = await repo.ListarAsync(
          new FiltroDeComponente("   ", false, 1, 500), CancellationToken.None);

      Assert.True(todos.Total >= 2);
      Assert.Contains(todos.Itens, c => c.Codigo == $"{prefixo}-a");
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }
}

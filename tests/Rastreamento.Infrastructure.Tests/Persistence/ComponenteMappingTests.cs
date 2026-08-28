using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>Requer o SQL Server no ar (docker compose up -d) com o schema aplicado.</summary>
/// <remarks>
/// Continua na <see cref="ColecaoQueEscreveEmComponente"/>, mas o motivo que a criou morreu em
/// 2026-08-22: <c>Busca_em_branco_nao_filtra_nada</c> nao compara mais dois <c>Total</c> globais,
/// e nenhum teste desta classe depende hoje do tamanho da tabela. A colecao ficou porque desliga-la
/// e decisao separada, que pede medicao propria — e porque ela nunca cobriu o caso que fazia o
/// teste falhar de verdade: quem escrevia na janela entre as duas consultas era outro PROCESSO
/// (<c>Api.Tests</c>), e <c>[Collection]</c> so serializa dentro do mesmo assembly.
/// </remarks>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class ComponenteMappingTests : TesteComBanco
{
  /// <summary>
  /// Prefixo unico por teste: as consultas de listagem filtram por ele, entao as assercoes de
  /// paginacao nao dependem de a tabela estar vazia nem de outros testes rodando em paralelo.
  /// </summary>
  private static string NovoPrefixo() => $"cmp-{Guid.NewGuid():N}";

  private static Componente Peca(string codigo, string descricao = "Suporte lateral") =>
      new() { Codigo = codigo, Descricao = descricao, Tipo = "Fabricado", Ativo = true };

  /// <summary>
  /// Recorta de um resultado de listagem so as linhas deste teste. Existe para assercao sobre
  /// listagem NAO escopada (busca nula ou em branco) nao depender do que outros processos
  /// escrevem em <c>dbo.Componente</c> ao mesmo tempo.
  /// </summary>
  private static string[] SoDoPrefixo(IEnumerable<Componente> itens, string prefixo) =>
      itens.Where(c => c.Codigo.StartsWith(prefixo, StringComparison.Ordinal))
          .Select(c => c.Codigo)
          .ToArray();

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
  public async Task ObterPorIdAsync_devolve_entidade_rastreada_que_persiste_sem_update_explicito()
  {
    // Mata a mutacao "acrescentar .AsNoTracking() em ObterPorIdAsync": Editar/DefinirAtivo (Tasks
    // 2/3) mutam a entidade devolvida por ObterPorIdAsync e chamam SalvarAlteracoesAsync SEM
    // nenhum Update/Attach explicito, contando com o change tracking do EF. Com AsNoTracking o
    // SaveChangesAsync vira no-op e a mutacao some em silencio — e exatamente essa cadeia
    // (Adicionar -> Obter -> mutar -> Salvar sem Update -> reler) que este teste reproduz.
    var prefixo = NovoPrefixo();
    var codigo = $"{prefixo}-a";
    int id;

    await using (var db = NovoContexto())
    {
      var repo = new ComponenteRepository(db);
      var componente = Peca(codigo);
      await repo.AdicionarAsync(componente, CancellationToken.None);
      await repo.SalvarAlteracoesAsync(CancellationToken.None);
      id = componente.Id;
    }

    try
    {
      await using (var db = NovoContexto())
      {
        var repo = new ComponenteRepository(db);
        var carregado = await repo.ObterPorIdAsync(id, CancellationToken.None);
        carregado!.Descricao = "Descricao alterada via change tracking";
        carregado.Ativo = false;
        // Sem Update/Attach aqui de proposito: e o que faz a asserção depender do tracking.
        await repo.SalvarAlteracoesAsync(CancellationToken.None);
      }

      await using (var db = NovoContexto())
      {
        var repo = new ComponenteRepository(db);
        var releitura = await repo.ObterPorIdAsync(id, CancellationToken.None);
        Assert.Equal("Descricao alterada via change tracking", releitura!.Descricao);
        Assert.False(releitura.Ativo);
      }
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }

  [Fact]
  public async Task ObterPorCodigoAsync_devolve_entidade_rastreada_que_persiste_sem_update_explicito()
  {
    // Mesma prova do teste acima, mas passando por ObterPorCodigoAsync: e um mutante DISTINTO
    // (.AsNoTracking() acrescentado especificamente nesse metodo) que o teste por Id nao mata,
    // porque ele nunca chama ObterPorCodigoAsync.
    var prefixo = NovoPrefixo();
    var codigo = $"{prefixo}-a";

    await using (var db = NovoContexto())
    {
      var repo = new ComponenteRepository(db);
      await repo.AdicionarAsync(Peca(codigo), CancellationToken.None);
      await repo.SalvarAlteracoesAsync(CancellationToken.None);
    }

    try
    {
      await using (var db = NovoContexto())
      {
        var repo = new ComponenteRepository(db);
        var carregado = await repo.ObterPorCodigoAsync(codigo, CancellationToken.None);
        carregado!.Descricao = "Descricao alterada via codigo";
        carregado.Ativo = false;
        await repo.SalvarAlteracoesAsync(CancellationToken.None);
      }

      await using (var db = NovoContexto())
      {
        var repo = new ComponenteRepository(db);
        var releitura = await repo.ObterPorCodigoAsync(codigo, CancellationToken.None);
        Assert.Equal("Descricao alterada via codigo", releitura!.Descricao);
        Assert.False(releitura.Ativo);
      }
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }

  [Fact]
  public async Task ObterPorCodigoAsync_ignora_caixa()
  {
    // A insensibilidade a caixa vem da COLLATION da coluna Codigo, nao de codigo C# (nao ha
    // ToUpper/ToLower em ComponenteRepository) — nenhum fake de repositorio prova essa
    // propriedade, so o banco real. Insere com um casing e busca com outro.
    var prefixo = NovoPrefixo();
    var codigoInserido = $"{prefixo}-MIX";

    await using (var db = NovoContexto())
    {
      db.Componentes.Add(Peca(codigoInserido));
      await db.SaveChangesAsync();
    }

    try
    {
      await using var db = NovoContexto();
      var repo = new ComponenteRepository(db);

      var achado = await repo.ObterPorCodigoAsync(codigoInserido.ToLowerInvariant(), CancellationToken.None);

      Assert.NotNull(achado);
      Assert.Equal(codigoInserido, achado!.Codigo);
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
  public async Task Busca_com_espacos_ao_redor_acha_o_mesmo_que_a_busca_sem_espacos()
  {
    // Mata a mutacao "var busca = filtro.Busca.Trim() -> var busca = filtro.Busca": sem o Trim(),
    // Contains("  marcador  ") com os espacos literais nao acha um Componente cuja Descricao
    // contem so "marcador", sem espaco ao redor. Nenhum teste ja existente cobre isso —
    // Busca_em_branco_nao_filtra_nada usa uma busca 100% de espacos, que a guarda
    // IsNullOrWhiteSpace pula ANTES de chegar no Trim(), e Busca_casa_no_codigo_e_na_descricao
    // nunca cerca o termo de espaco.
    var prefixo = NovoPrefixo();
    var marcador = $"m{Guid.NewGuid():N}"[..10];
    await using (var db = NovoContexto())
    {
      db.Componentes.Add(Peca($"{prefixo}-a", $"Descricao com {marcador} dentro"));
      await db.SaveChangesAsync();
    }

    try
    {
      await using var db = NovoContexto();
      var repo = new ComponenteRepository(db);

      var semEspacos = await repo.ListarAsync(
          new FiltroDeComponente(marcador, false, 1, 20), CancellationToken.None);
      var comEspacos = await repo.ListarAsync(
          new FiltroDeComponente($"  {marcador}  ", false, 1, 20), CancellationToken.None);

      Assert.Equal(1, semEspacos.Total);
      Assert.Equal(semEspacos.Total, comEspacos.Total);
      Assert.Equal(
          semEspacos.Itens.Select(c => c.Codigo).ToArray(),
          comEspacos.Itens.Select(c => c.Codigo).ToArray());
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
    // O comentario antigo aqui afirmava que, se o repositorio testasse so `!= null`, um
    // `busca: "   "` viraria `Contains("   ")` e devolveria zero. A medicao por mutacao
    // desmentiu isso (Task 1, mutacao 5): o Trim() roda incondicionalmente DENTRO do if, entao
    // "   ".Trim() vira "" de qualquer jeito, e Contains("") traduzido pro SQL Server vira
    // `LIKE '%%'`, que casa com tudo — mesmo efeito observavel de nao filtrar. Ou seja essa troca
    // de guarda especifica e mutante EQUIVALENTE: nenhum teste de caixa-preta sobre o resultado
    // de ListarAsync discrimina essa mutacao sem mudar a estrutura do codigo de producao (ex.:
    // tirar o Trim() de dentro do if, o que so pioraria o codigo). O que este teste prova, em vez
    // disso, e o comportamento OBSERVAVEL: busca so de espacos se comporta como busca ausente.
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

      // Tamanho = int.MaxValue: uma pagina so, a tabela inteira. Este teste nao e sobre paginacao
      // (isso e Pagina_alem_do_fim_devolve_lista_vazia_e_o_total_verdadeiro), e pedir tudo numa
      // pagina e o que faz as MINHAS linhas aparecerem no resultado independentemente de quantas
      // linhas vem antes delas em OrderBy(Codigo). Foi exatamente por isso que a primeira versao
      // escopada por prefixo foi abandonada: ela olhava as primeiras 500 e a linha do prefixo
      // podia nao caber ali. Uma pagina infinita nao tem esse problema; o custo e carregar a
      // tabela de dev inteira (dezenas de linhas) em memoria.
      const int paginaUnica = int.MaxValue;

      var semBusca = await repo.ListarAsync(
          new FiltroDeComponente(null, false, 1, paginaUnica), CancellationToken.None);
      var comEspacos = await repo.ListarAsync(
          new FiltroDeComponente("   ", false, 1, paginaUnica), CancellationToken.None);

      // A prova, escopada nas linhas que este teste controla: as duas nao contem tres espacos
      // seguidos nem no Codigo nem na Descricao, entao uma busca "   " aplicada ao pe da letra as
      // faria sumir. Elas aparecerem nas DUAS listas e o comportamento observavel de "busca so de
      // espacos == busca ausente". Mata a mutacao real (guarda IsNullOrWhiteSpace -> IsNullOrEmpty
      // com o Trim fora do if): medido, com esse mutante a lista com espacos volta sem nenhuma das
      // duas linhas do prefixo — Expected [..-a, ..-b], Actual [].
      //
      // Ate 2026-08-22 isto era `Assert.Equal(semBusca.Total, comEspacos.Total)` sobre os dois
      // Total GLOBAIS, e era corrida: qualquer INSERT/DELETE de OUTRO PROCESSO (Api.Tests) na
      // janela entre as duas consultas muda a contagem e a assercao quebra. Reproduzido de
      // proposito: 11 vermelhas em 30 com escrita concorrente na tabela, nos dois sentidos
      // (54 -> 55 por insert, 55 -> 54 por delete). [Collection] nao resolve isso, porque so
      // serializa classes dentro do MESMO assembly.
      var meus = new[] { $"{prefixo}-a", $"{prefixo}-b" };
      Assert.Equal(meus, SoDoPrefixo(semBusca.Itens, prefixo));
      Assert.Equal(meus, SoDoPrefixo(comEspacos.Itens, prefixo));

      // Dos Total globais so se afirma o que e monotono — nenhuma das duas consultas devolveu
      // menos que as minhas duas linhas. Diferente da igualdade entre eles, isto e imune a
      // escrita concorrente: as minhas linhas estao commitadas antes e apagadas depois.
      Assert.True(semBusca.Total >= 2);
      Assert.True(comEspacos.Total >= 2);
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }
}

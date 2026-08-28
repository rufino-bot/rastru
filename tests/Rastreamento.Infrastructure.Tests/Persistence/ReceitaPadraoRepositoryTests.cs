using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// O repositorio da receita padrao contra o SQL Server REAL. O que se prova aqui e a SEMANTICA DE
/// SUBSTITUICAO — apagar as linhas antigas do componente e gravar as novas numa unica transacao —
/// e o ESCOPO desse apagamento, nas tres tabelas. Banco em memoria nao provaria: o ponto e o
/// comportamento transacional, as FKs de verdade e o que acontece com dois escritores simultaneos.
/// </summary>
/// <remarks>
/// Na mesma <see cref="ColecaoQueEscreveEmComponente"/> que os outros escritores de
/// <c>dbo.Componente</c>: ver o XML doc de la para o porque.
/// </remarks>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class ReceitaPadraoRepositoryTests : FixtureDeReceitaPadrao
{
  /// <summary>
  /// Apaga, em ordem de FK, tudo o que um teste deste arquivo cria. As linhas de receita saem
  /// antes dos Componentes, Setores e Materiais que elas referenciam — a ordem inversa viola a FK.
  ///
  /// Recebe conjuntos, e nao um par fixo, porque cada teste daqui cria uma quantidade diferente
  /// de linhas de apoio (pai + dois filhos, dois pais + um filho, componente + dois materiais...).
  /// Sem isto cada execucao contra o banco de dev deixaria linha orfa acumulando sem limite.
  /// </summary>
  private static async Task LimparAsync(
      int[] componenteIds, int[]? setorIds = null, int[]? materialIds = null)
  {
    setorIds ??= [];
    materialIds ??= [];

    await using var db = NovoContexto();

    db.FilhosPadrao.RemoveRange(await db.FilhosPadrao
        .Where(f => componenteIds.Contains(f.ComponentePaiId)
                 || componenteIds.Contains(f.ComponenteFilhoId))
        .ToListAsync());
    db.MateriaisPadrao.RemoveRange(await db.MateriaisPadrao
        .Where(m => componenteIds.Contains(m.ComponenteId)).ToListAsync());
    db.RoteirosPadrao.RemoveRange(await db.RoteirosPadrao
        .Where(r => componenteIds.Contains(r.ComponenteId)).ToListAsync());
    await db.SaveChangesAsync();

    db.Componentes.RemoveRange(
        await db.Componentes.Where(c => componenteIds.Contains(c.Id)).ToListAsync());
    db.Setores.RemoveRange(await db.Setores.Where(s => setorIds.Contains(s.Id)).ToListAsync());
    db.Materiais.RemoveRange(
        await db.Materiais.Where(m => materialIds.Contains(m.Id)).ToListAsync());
    await db.SaveChangesAsync();
  }

  /// <summary>
  /// Um id que com CERTEZA nao existe: cria a linha e a apaga. IDENTITY nao reaproveita valor,
  /// entao o id fica livre para sempre — mais confiavel que chutar um numero alto, que o banco de
  /// dev compartilhado pode ter alcancado.
  /// </summary>
  private static async Task<int> IdDeComponenteQueNaoExiste()
  {
    await using var db = NovoContexto();
    var componente = await UmComponente(db);
    db.Componentes.Remove(componente);
    await db.SaveChangesAsync();
    return componente.Id;
  }

  private static async Task<int> IdDeMaterialQueNaoExiste()
  {
    await using var db = NovoContexto();
    var material = await UmMaterial(db);
    db.Materiais.Remove(material);
    await db.SaveChangesAsync();
    return material.Id;
  }

  private static async Task<int> IdDeSetorQueNaoExiste()
  {
    await using var db = NovoContexto();
    var setor = await UmSetor(db);
    db.Setores.Remove(setor);
    await db.SaveChangesAsync();
    return setor.Id;
  }

  private static ComponenteFilhoPadrao Aresta(int paiId, int filhoId, decimal quantidade) =>
      new() { ComponentePaiId = paiId, ComponenteFilhoId = filhoId, QuantidadePadrao = quantidade };

  /// <summary>
  /// A substituicao e o coracao do contrato: POST significa "a receita passa a ser EXATAMENTE
  /// estas linhas". As antigas somem, as novas entram, numa transacao so.
  ///
  /// O componente `outro` tem receita propria de proposito e NAO e decoracao: sem uma linha de
  /// outro pai na tabela, "filtrou pelo pai certo" e "nao filtrou nada" produzem a mesma contagem,
  /// e a mutacao "ListarFilhosAsync ignora o componenteId" passa verde (foi o que a review mediu).
  /// </summary>
  [Fact]
  public async Task Substituir_filhos_apaga_as_linhas_antigas_e_grava_as_novas()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var pai = await UmComponente(db);
    var outro = await UmComponente(db);
    var filhoA = await UmComponente(db);
    var filhoB = await UmComponente(db);

    try
    {
      await repo.SubstituirFilhosAsync(
          outro.Id, [Aresta(outro.Id, filhoA.Id, 3m)], CancellationToken.None);

      await repo.SubstituirFilhosAsync(
          pai.Id, [Aresta(pai.Id, filhoA.Id, 1m)], CancellationToken.None);

      await repo.SubstituirFilhosAsync(
          pai.Id, [Aresta(pai.Id, filhoB.Id, 7m)], CancellationToken.None);

      var linhas = await repo.ListarFilhosAsync(pai.Id, CancellationToken.None);

      var unica = Assert.Single(linhas);
      Assert.Equal(filhoB.Id, unica.ComponenteFilhoId);
      Assert.Equal(7m, unica.QuantidadePadrao);
      Assert.All(linhas, l => Assert.Equal(pai.Id, l.ComponentePaiId));
    }
    finally
    {
      await LimparAsync([pai.Id, outro.Id, filhoA.Id, filhoB.Id]);
    }
  }

  /// <summary>
  /// Substituir uma receita de DUAS linhas por uma: e o caso que separa "apaga as linhas antigas"
  /// de "apaga a primeira linha antiga". Todos os outros testes de substituicao partem de 0 ou 1
  /// linha, e por isso um delete com `.Take(1)` passava verde.
  /// </summary>
  [Fact]
  public async Task Substituir_filhos_troca_receita_de_duas_linhas_por_uma()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var pai = await UmComponente(db);
    var filhoA = await UmComponente(db);
    var filhoB = await UmComponente(db);
    var filhoC = await UmComponente(db);

    try
    {
      await repo.SubstituirFilhosAsync(pai.Id, [
        Aresta(pai.Id, filhoA.Id, 1m),
        Aresta(pai.Id, filhoB.Id, 2m),
      ], CancellationToken.None);

      Assert.Equal(2, (await repo.ListarFilhosAsync(pai.Id, CancellationToken.None)).Count);

      await repo.SubstituirFilhosAsync(
          pai.Id, [Aresta(pai.Id, filhoC.Id, 9m)], CancellationToken.None);

      var linhas = await repo.ListarFilhosAsync(pai.Id, CancellationToken.None);

      var unica = Assert.Single(linhas);
      Assert.Equal(filhoC.Id, unica.ComponenteFilhoId);
      Assert.Equal(9m, unica.QuantidadePadrao);
      Assert.All(linhas, l => Assert.Equal(pai.Id, l.ComponentePaiId));
    }
    finally
    {
      await LimparAsync([pai.Id, filhoA.Id, filhoB.Id, filhoC.Id]);
    }
  }

  /// <summary>Lista vazia APAGA — e o unico caminho de remocao que existe (nao ha DELETE).</summary>
  [Fact]
  public async Task Substituir_filhos_com_lista_vazia_apaga_tudo()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var pai = await UmComponente(db);
    var outro = await UmComponente(db);
    var filho = await UmComponente(db);

    try
    {
      await repo.SubstituirFilhosAsync(
          outro.Id, [Aresta(outro.Id, filho.Id, 5m)], CancellationToken.None);
      await repo.SubstituirFilhosAsync(
          pai.Id, [Aresta(pai.Id, filho.Id, 1m)], CancellationToken.None);

      await repo.SubstituirFilhosAsync(pai.Id, [], CancellationToken.None);

      // Vazio para o pai E a linha do outro intacta: a segunda metade e o que distingue "apagou
      // o que era do pai" de "apagou a tabela".
      Assert.Empty(await repo.ListarFilhosAsync(pai.Id, CancellationToken.None));
      var doOutro = Assert.Single(await repo.ListarFilhosAsync(outro.Id, CancellationToken.None));
      Assert.Equal(outro.Id, doOutro.ComponentePaiId);
    }
    finally
    {
      await LimparAsync([pai.Id, outro.Id, filho.Id]);
    }
  }

  /// <summary>
  /// A substituicao e ESCOPADA ao componente: mexer na receita de A nao pode tocar na de B.
  /// Sem o `Where(ComponentePaiId == id)` no delete, este teste morre.
  /// </summary>
  [Fact]
  public async Task Substituir_filhos_nao_toca_na_receita_de_outro_componente()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var paiA = await UmComponente(db);
    var paiB = await UmComponente(db);
    var filho = await UmComponente(db);

    try
    {
      await repo.SubstituirFilhosAsync(
          paiB.Id, [Aresta(paiB.Id, filho.Id, 4m)], CancellationToken.None);

      await repo.SubstituirFilhosAsync(paiA.Id, [], CancellationToken.None);

      var doB = Assert.Single(await repo.ListarFilhosAsync(paiB.Id, CancellationToken.None));
      Assert.Equal(paiB.Id, doB.ComponentePaiId);
      Assert.Equal(filho.Id, doB.ComponenteFilhoId);

      // A leitura de paiA tem de ser vazia MESMO com paiB tendo linha: e isto que morre se
      // ListarFilhosAsync ignorar o componenteId.
      Assert.Empty(await repo.ListarFilhosAsync(paiA.Id, CancellationToken.None));
    }
    finally
    {
      await LimparAsync([paiA.Id, paiB.Id, filho.Id]);
    }
  }

  /// <summary>
  /// ATOMICIDADE, medida e nao declarada. O INSERT estoura (FK invalida) DEPOIS de o DELETE ja
  /// ter apagado a linha antiga — se o DELETE nao estivesse na mesma transacao que o INSERT, ou
  /// se a transacao fosse partida em dois commits, o componente ficaria com receita VAZIA. Este
  /// e o teste que a frase "meio-termo nao e estado alcancavel" precisava ter desde o inicio.
  /// </summary>
  [Fact]
  public async Task Substituir_filhos_que_estoura_no_meio_deixa_as_linhas_antigas_intactas()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var pai = await UmComponente(db);
    var filho = await UmComponente(db);

    try
    {
      await repo.SubstituirFilhosAsync(
          pai.Id, [Aresta(pai.Id, filho.Id, 3m)], CancellationToken.None);

      await Assert.ThrowsAsync<DbUpdateException>(() => repo.SubstituirFilhosAsync(
          pai.Id, [Aresta(pai.Id, -1, 1m)], CancellationToken.None));

      // Contexto NOVO de proposito: o que interessa e o que ficou no BANCO, e o contexto que
      // falhou ainda carrega a entidade ruim como `Added` no ChangeTracker.
      await using var dbLeitura = NovoContexto();
      var linhas = await new ReceitaPadraoRepository(dbLeitura)
          .ListarFilhosAsync(pai.Id, CancellationToken.None);

      var unica = Assert.Single(linhas);
      Assert.Equal(filho.Id, unica.ComponenteFilhoId);
      Assert.Equal(3m, unica.QuantidadePadrao);
    }
    finally
    {
      await LimparAsync([pai.Id, filho.Id]);
    }
  }

  /// <summary>
  /// CONCORRENCIA. Duas substituicoes simultaneas do MESMO componente, sobre receita vazia — o
  /// caso em que o desenho anterior (apagar pelas PKs das linhas lidas) deixava a UNIAO das duas
  /// gravacoes, sem erro nenhum, medido 3 de 3 na review.
  ///
  /// O que se afirma aqui e o contrato: a receita final e a de UM dos escritores. Se o banco
  /// derrubar um deles (deadlock/timeout do range lock), isso e desfecho legitimo e esta afirmado
  /// explicitamente — o que nao pode acontecer e os dois "vencerem".
  /// </summary>
  [Fact]
  public async Task Substituicoes_paralelas_do_mesmo_componente_nao_deixam_a_uniao()
  {
    await using var db = NovoContexto();
    var pai = await UmComponente(db);
    var filhoX = await UmComponente(db);
    var filhoY = await UmComponente(db);

    try
    {
      await using var dbA = NovoContexto();
      await using var dbB = NovoContexto();

      var tarefaA = new ReceitaPadraoRepository(dbA).SubstituirFilhosAsync(
          pai.Id, [Aresta(pai.Id, filhoX.Id, 1m)], CancellationToken.None);
      var tarefaB = new ReceitaPadraoRepository(dbB).SubstituirFilhosAsync(
          pai.Id, [Aresta(pai.Id, filhoY.Id, 2m)], CancellationToken.None);

      var vencedores = 0;
      foreach (var tarefa in new[] { tarefaA, tarefaB })
      {
        try
        {
          await tarefa;
          vencedores++;
        }
        catch (Exception e)
            when (e is DbUpdateException or SqlException or ConflitoDeConcorrenciaException)
        {
          // Perdedor derrubado pelo banco (deadlock do range lock, ou conflito no INSERT):
          // desfecho LEGITIMO e afirmado, e o oposto da uniao silenciosa. Deadlock e lock timeout
          // chegam ja traduzidos em ConflitoDeConcorrenciaException; os outros erros sobem crus
          // (SqlException quando vem do DELETE do ExecuteDelete, que nao passa pelo SaveChanges).
        }
      }

      Assert.True(vencedores >= 1, "os dois escritores falharam — nenhum gravou");

      await using var dbLeitura = NovoContexto();
      var linhas = await new ReceitaPadraoRepository(dbLeitura)
          .ListarFilhosAsync(pai.Id, CancellationToken.None);

      var unica = Assert.Single(linhas);
      Assert.True(
          unica.ComponenteFilhoId == filhoX.Id || unica.ComponenteFilhoId == filhoY.Id,
          $"a linha que sobrou ({unica.ComponenteFilhoId}) nao e de nenhum dos dois escritores");
    }
    finally
    {
      await LimparAsync([pai.Id, filhoX.Id, filhoY.Id]);
    }
  }

  /// <summary>
  /// CONCORRENCIA, o lado da TRADUCAO. O perdedor de duas gravacoes simultaneas sobe
  /// <see cref="ConflitoDeConcorrenciaException"/> — nao <c>SqlException</c> cru — para que a
  /// Application devolva 409 sem referenciar o EF Core, como ja acontece no fluxo de refresh
  /// token. Sem isto, o desfecho que o proprio desenho do SERIALIZABLE preve como legitimo saia
  /// como 500 nao tratado.
  ///
  /// A corrida e DETERMINISTICA, e nao "roda duas tarefas e torce": uma conexao segura a faixa do
  /// componente numa transacao SERIALIZABLE aberta, e a conexao do repositorio entra com
  /// <c>SET LOCK_TIMEOUT 0</c> — o gerenciador de lock a derruba na hora com o erro 1222, irmao do
  /// 1205 (deadlock victim) que a mesma guarda reconhece. Um teste que dependesse de um deadlock
  /// real seria intermitente e, quando nao deadlockasse, passaria sem provar nada.
  ///
  /// O lado oposto — nao engolir erro que NAO e de concorrencia — e provado por
  /// <c>Substituir_filhos_que_estoura_no_meio_deixa_as_linhas_antigas_intactas</c>, que continua
  /// exigindo <c>DbUpdateException</c> crua para violacao de FK.
  /// </summary>
  [Fact]
  public async Task Perdedor_de_gravacao_simultanea_sobe_como_conflito_de_concorrencia()
  {
    await using var db = NovoContexto();
    var componente = await UmComponente(db);
    var material = await UmMaterial(db);

    try
    {
      // Conexao que SEGURA a faixa do componente: SERIALIZABLE + leitura da faixa vazia = range
      // lock preso ate o rollback.
      await using var bloqueador = new SqlConnection(Conn);
      await bloqueador.OpenAsync();
      await using var tx = (SqlTransaction)await bloqueador.BeginTransactionAsync(
          System.Data.IsolationLevel.Serializable);
      await using (var leitura = bloqueador.CreateCommand())
      {
        leitura.Transaction = tx;
        leitura.CommandText =
            "SELECT Id FROM dbo.ComponenteMaterialPadrao WHERE ComponenteId = @id";
        leitura.Parameters.AddWithValue("@id", componente.Id);
        await leitura.ExecuteNonQueryAsync();
      }

      await using var dbPerdedor = NovoContexto();
      await dbPerdedor.Database.OpenConnectionAsync();
      // Sem isto o teste ficaria pendurado no lock ate o timeout default (infinito).
      await dbPerdedor.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 0");

      await Assert.ThrowsAsync<ConflitoDeConcorrenciaException>(
          () => new ReceitaPadraoRepository(dbPerdedor).SubstituirMateriaisAsync(
              componente.Id,
              [new ComponenteMaterialPadrao
              {
                ComponenteId = componente.Id,
                MaterialId = material.Id,
                QuantidadePadrao = 1m,
              }],
              CancellationToken.None));

      await tx.RollbackAsync();
    }
    finally
    {
      await LimparAsync([componente.Id], materialIds: [material.Id]);
    }
  }

  /// <summary>
  /// O roteiro sai ORDENADO por Ordem — a tela depende disso para desenhar a sequencia. O passo
  /// do componente `outro` esta la para a assercao nao ser satisfeita por "nao filtrou nada".
  /// </summary>
  [Fact]
  public async Task Listar_roteiro_devolve_ordenado_por_ordem()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var componente = await UmComponente(db);
    var outro = await UmComponente(db);
    var setorA = await UmSetor(db);
    var setorB = await UmSetor(db);

    try
    {
      await repo.SubstituirRoteiroAsync(outro.Id, [
        new ComponenteRoteiroPadrao { ComponenteId = outro.Id, SetorId = setorB.Id, Ordem = 1 },
      ], CancellationToken.None);

      // Inseridos FORA de ordem de proposito: se o repositorio nao ordenar, o teste pega.
      await repo.SubstituirRoteiroAsync(componente.Id, [
        new ComponenteRoteiroPadrao { ComponenteId = componente.Id, SetorId = setorB.Id, Ordem = 2 },
        new ComponenteRoteiroPadrao { ComponenteId = componente.Id, SetorId = setorA.Id, Ordem = 1 },
      ], CancellationToken.None);

      var linhas = await repo.ListarRoteiroAsync(componente.Id, CancellationToken.None);

      Assert.Equal([setorA.Id, setorB.Id], linhas.Select(l => l.SetorId));
      Assert.All(linhas, l => Assert.Equal(componente.Id, l.ComponenteId));
    }
    finally
    {
      await LimparAsync([componente.Id, outro.Id], [setorA.Id, setorB.Id]);
    }
  }

  /// <summary>
  /// O escopo do delete de ROTEIRO, atacado no proprio call site. O generico `Substituir` ja tem
  /// guarda pelo caminho dos filhos, mas um `Where` errado nesta chamada especifica arrasava a
  /// tabela inteira sem a suite piscar (medido na review: a mutacao apagou dado real).
  /// </summary>
  [Fact]
  public async Task Substituir_roteiro_nao_toca_no_roteiro_de_outro_componente()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var componente = await UmComponente(db);
    var outro = await UmComponente(db);
    var setor = await UmSetor(db);

    try
    {
      await repo.SubstituirRoteiroAsync(outro.Id, [
        new ComponenteRoteiroPadrao { ComponenteId = outro.Id, SetorId = setor.Id, Ordem = 1 },
      ], CancellationToken.None);
      await repo.SubstituirRoteiroAsync(componente.Id, [
        new ComponenteRoteiroPadrao { ComponenteId = componente.Id, SetorId = setor.Id, Ordem = 1 },
      ], CancellationToken.None);

      await repo.SubstituirRoteiroAsync(componente.Id, [], CancellationToken.None);

      Assert.Empty(await repo.ListarRoteiroAsync(componente.Id, CancellationToken.None));
      var passo = Assert.Single(await repo.ListarRoteiroAsync(outro.Id, CancellationToken.None));
      Assert.Equal(outro.Id, passo.ComponenteId);
      Assert.Equal(setor.Id, passo.SetorId);
    }
    finally
    {
      await LimparAsync([componente.Id, outro.Id], [setor.Id]);
    }
  }

  /// <summary>
  /// Leitura + substituicao de MATERIAIS. O metodo nao era invocado por teste algum, e por isso
  /// "nao grava nada", "ignora o componenteId na leitura" e "apaga a tabela de materiais inteira"
  /// passavam verde os tres.
  /// </summary>
  [Fact]
  public async Task Substituir_materiais_apaga_as_antigas_e_nao_toca_em_outro_componente()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var componente = await UmComponente(db);
    var outro = await UmComponente(db);
    var materialA = await UmMaterial(db);
    var materialB = await UmMaterial(db);

    try
    {
      await repo.SubstituirMateriaisAsync(outro.Id, [
        new ComponenteMaterialPadrao
        {
          ComponenteId = outro.Id, MaterialId = materialA.Id, QuantidadePadrao = 9m,
        },
      ], CancellationToken.None);

      await repo.SubstituirMateriaisAsync(componente.Id, [
        new ComponenteMaterialPadrao
        {
          ComponenteId = componente.Id, MaterialId = materialA.Id, QuantidadePadrao = 2m,
        },
      ], CancellationToken.None);

      await repo.SubstituirMateriaisAsync(componente.Id, [
        new ComponenteMaterialPadrao
        {
          ComponenteId = componente.Id, MaterialId = materialB.Id, QuantidadePadrao = 5m,
        },
      ], CancellationToken.None);

      var linhas = await repo.ListarMateriaisAsync(componente.Id, CancellationToken.None);

      var unica = Assert.Single(linhas);
      Assert.Equal(materialB.Id, unica.MaterialId);
      Assert.Equal(5m, unica.QuantidadePadrao);
      Assert.All(linhas, l => Assert.Equal(componente.Id, l.ComponenteId));

      var doOutro = Assert.Single(
          await repo.ListarMateriaisAsync(outro.Id, CancellationToken.None));
      Assert.Equal(outro.Id, doOutro.ComponenteId);
      Assert.Equal(materialA.Id, doOutro.MaterialId);
      Assert.Equal(9m, doOutro.QuantidadePadrao);
    }
    finally
    {
      await LimparAsync([componente.Id, outro.Id], materialIds: [materialA.Id, materialB.Id]);
    }
  }

  /// <summary>
  /// A deteccao de ciclo (Task 5) precisa do grafo INTEIRO, nao so das linhas de um componente.
  /// </summary>
  [Fact]
  public async Task Listar_todas_as_arestas_traz_linhas_de_componentes_diferentes()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var paiA = await UmComponente(db);
    var paiB = await UmComponente(db);
    var filho = await UmComponente(db);

    try
    {
      await repo.SubstituirFilhosAsync(
          paiA.Id, [Aresta(paiA.Id, filho.Id, 1m)], CancellationToken.None);
      await repo.SubstituirFilhosAsync(
          paiB.Id, [Aresta(paiB.Id, filho.Id, 1m)], CancellationToken.None);

      var arestas = await repo.ListarTodasAsArestasAsync(CancellationToken.None);

      Assert.Contains(arestas, a => a.ComponentePaiId == paiA.Id);
      Assert.Contains(arestas, a => a.ComponentePaiId == paiB.Id);
    }
    finally
    {
      await LimparAsync([paiA.Id, paiB.Id, filho.Id]);
    }
  }

  /// <summary>
  /// O contrato de <c>ObterComponenteAsync</c> e "null quando nao existe" — e o caso de uso
  /// traduz isso para 404. Sem o par existente/inexistente, tanto "devolve sempre null" quanto
  /// "ignora o id e devolve o primeiro" passavam.
  /// </summary>
  [Fact]
  public async Task Obter_componente_traz_o_existente_e_null_para_id_inexistente()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var componente = await UmComponente(db);
    var idInexistente = await IdDeComponenteQueNaoExiste();

    try
    {
      var achado = await repo.ObterComponenteAsync(componente.Id, CancellationToken.None);

      Assert.NotNull(achado);
      Assert.Equal(componente.Id, achado.Id);
      Assert.Equal(componente.Codigo, achado.Codigo);

      Assert.Null(await repo.ObterComponenteAsync(idInexistente, CancellationToken.None));
    }
    finally
    {
      await LimparAsync([componente.Id]);
    }
  }

  /// <summary>
  /// "Devolve SO os que existem" e o que permite ao caso de uso descobrir os ausentes pela
  /// diferenca (Task 3). O segundo componente, FORA da lista de ids, e o que distingue "filtrou"
  /// de "devolveu a tabela".
  /// </summary>
  [Fact]
  public async Task Obter_componentes_por_id_traz_so_os_existentes()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var pedido = await UmComponente(db);
    var foraDaLista = await UmComponente(db);
    var idInexistente = await IdDeComponenteQueNaoExiste();

    try
    {
      var achados = await repo.ObterComponentesPorIdAsync(
          [pedido.Id, idInexistente], CancellationToken.None);

      var unico = Assert.Single(achados);
      Assert.Equal(pedido.Id, unico.Id);
      Assert.DoesNotContain(achados, c => c.Id == foraDaLista.Id);
    }
    finally
    {
      await LimparAsync([pedido.Id, foraDaLista.Id]);
    }
  }

  /// <summary>Mesmo contrato do anterior, para Material — a Task 3 valida os dois catalogos.</summary>
  [Fact]
  public async Task Obter_materiais_por_id_traz_so_os_existentes()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var pedido = await UmMaterial(db);
    var foraDaLista = await UmMaterial(db);
    var idInexistente = await IdDeMaterialQueNaoExiste();

    try
    {
      var achados = await repo.ObterMateriaisPorIdAsync(
          [pedido.Id, idInexistente], CancellationToken.None);

      var unico = Assert.Single(achados);
      Assert.Equal(pedido.Id, unico.Id);
      Assert.DoesNotContain(achados, m => m.Id == foraDaLista.Id);
    }
    finally
    {
      await LimparAsync([], materialIds: [pedido.Id, foraDaLista.Id]);
    }
  }

  /// <summary>Mesmo contrato do anterior, para Setor — e o que a Task 4 (roteiro) vai usar.</summary>
  [Fact]
  public async Task Obter_setores_por_id_traz_so_os_existentes()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var pedido = await UmSetor(db);
    var foraDaLista = await UmSetor(db);
    var idInexistente = await IdDeSetorQueNaoExiste();

    try
    {
      var achados = await repo.ObterSetoresPorIdAsync(
          [pedido.Id, idInexistente], CancellationToken.None);

      var unico = Assert.Single(achados);
      Assert.Equal(pedido.Id, unico.Id);
      Assert.DoesNotContain(achados, s => s.Id == foraDaLista.Id);
    }
    finally
    {
      await LimparAsync([], [pedido.Id, foraDaLista.Id]);
    }
  }
}

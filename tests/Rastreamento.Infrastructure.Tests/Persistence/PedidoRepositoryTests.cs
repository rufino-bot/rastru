using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// `PedidoRepository` contra o SQL Server real: busca, filtro por status e por Material, ordem e
/// total da pagina, os mais antigos e os Materiais em uso. Cada teste cria os SEUS Pedidos, com um
/// texto unico (cliente ou parte do numero) que a busca usa, e afirma so sobre os Ids que criou —
/// nunca contagem global de tabela compartilhada.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class PedidoRepositoryTests : TesteComBanco
{
  private static string Unico() => Guid.NewGuid().ToString("N")[..12];

  private static FiltroDePedidos Filtro(
      string? busca = null, string[]? status = null, int[]? materiais = null, int pagina = 1, int tamanho = 100,
      OrdemDePedidos ordem = OrdemDePedidos.Recentes) =>
      new(busca, status ?? [], materiais ?? [], pagina, tamanho, ordem);

  /// <summary>Pedido solto (sem Agrupamento); a limpeza dele e <see cref="ApagarPedidosAsync"/>.</summary>
  private static async Task<int> NovoPedidoAsync(
      RastreamentoDbContext db, string numero, string cliente, string status = "Aberto", DateTime? dataAbertura = null,
      DateOnly? dataEntrega = null)
  {
    var autor = (await db.Usuarios.AsNoTracking().SingleAsync(u => u.NomeUsuario == "admin")).Id;
    var pedido = new Pedido
    {
      Numero = numero, Cliente = cliente, Tipo = "Fabricacao", Status = status,
      DataAbertura = dataAbertura ?? DateTime.UtcNow, DataEntrega = dataEntrega ?? new DateOnly(2026, 10, 22),
      CriadoPorUsuarioId = autor,
    };
    db.Pedidos.Add(pedido);
    await db.SaveChangesAsync();
    return pedido.Id;
  }

  private async Task ApagarPedidosAsync(IEnumerable<int> ids)
  {
    await using var db = NovoContexto();
    foreach (var id in ids)
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Pedido WHERE Id = {id}");
  }

  /// <summary>Arvore com o cliente e o status dados no Pedido dela (a arvore nasce `Aberto` de "Cliente de teste").</summary>
  private static async Task<ArvoreDeTesteNoBanco> ArvoreComPedidoAsync(
      RastreamentoDbContext db, string rotulo, string cliente, string status)
  {
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, rotulo);
    await db.Pedidos.Where(p => p.Id == arvore.PedidoId)
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Cliente, cliente).SetProperty(p => p.Status, status));
    return arvore;
  }

  /// <summary>Limpa da ultima para a primeira: a arvore que ganhou o Material e a primeira, e as outras o referenciam.</summary>
  private async Task LimparArvoresAsync(List<ArvoreDeTesteNoBanco> arvores)
  {
    for (var i = arvores.Count - 1; i >= 0; i--)
      await arvores[i].LimparAsync(NovoContexto);
  }

  [Fact]
  public async Task Busca_acha_pelo_numero_pelo_cliente_e_pelo_codigo_do_Componente_de_um_no_filho()
  {
    var tok = Unico();
    await using var db = NovoContexto();
    var soltos = new List<int>();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "busca");
    try
    {
      var peloNumero = await NovoPedidoAsync(db, $"N-{tok}", "Cliente qualquer");
      var peloCliente = await NovoPedidoAsync(db, "C-" + Unico(), $"Metalurgica {tok}");
      var semNada = await NovoPedidoAsync(db, "S-" + Unico(), "Outro cliente");
      soltos.AddRange([peloNumero, peloCliente, semNada]);
      // O texto so aparece no Componente de um no FILHO: a Peca da arvore tem o Componente dela.
      var peca = await arvore.NovaPecaAsync(db, 1m);
      await arvore.NovoItemComComponenteAsync(db, peca, $"COD-{tok}-X", 1m, 1m);

      var (itens, total) = await new PedidoRepository(db).ListarAsync(Filtro(busca: tok), CancellationToken.None);

      Assert.Equal(
          new[] { peloNumero, peloCliente, arvore.PedidoId }.Order(),
          itens.Select(p => p.Id).Order());
      Assert.Equal(3, total);
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
      await ApagarPedidosAsync(soltos);
    }
  }

  [Fact]
  public async Task Busca_trata_curinga_de_LIKE_como_texto()
  {
    var tok = Unico();
    await using var db = NovoContexto();
    var ids = new List<int>();
    try
    {
      var porcento = await NovoPedidoAsync(db, $"A50%B-{tok}", "Cliente");
      var semPorcento = await NovoPedidoAsync(db, $"A50xB-{tok}", "Cliente");
      var colchete = await NovoPedidoAsync(db, $"A[x]C-{tok}", "Cliente");
      ids.AddRange([porcento, semPorcento, colchete]);
      var meus = ids.ToHashSet();
      var repo = new PedidoRepository(db);

      async Task<List<int>> AchadosPorAsync(string busca) =>
          (await repo.ListarAsync(Filtro(busca: busca), CancellationToken.None)).Itens
              .Select(p => p.Id).Where(meus.Contains).ToList();

      // "%" e "[x]" seriam curinga em LIKE cru: "50%" casaria os dois primeiros, "[x]" casaria o "x" solto.
      Assert.Equal([porcento], await AchadosPorAsync("50%"));
      Assert.Equal([colchete], await AchadosPorAsync("[x]"));
      // Nenhum dos tres tem sublinhado; "_" cru casaria qualquer caractere.
      Assert.Empty(await AchadosPorAsync("_"));
    }
    finally
    {
      await ApagarPedidosAsync(ids);
    }
  }

  [Fact]
  public async Task Busca_ignora_acento_e_caixa()
  {
    var tok = Unico();
    await using var db = NovoContexto();
    var ids = new List<int>();
    try
    {
      var acentuado = await NovoPedidoAsync(db, "A-" + Unico(), $"Metal\u00fargica \u00c1vila {tok}");
      var outro = await NovoPedidoAsync(db, "B-" + Unico(), $"Usinagem Bela {tok}");
      ids.AddRange([acentuado, outro]);
      var meus = ids.ToHashSet();
      var repo = new PedidoRepository(db);

      async Task<List<int>> AchadosPorAsync(string busca) =>
          (await repo.ListarAsync(Filtro(busca: busca), CancellationToken.None)).Itens
              .Select(p => p.Id).Where(meus.Contains).ToList();

      // Sem acento e em minuscula (celular): a collation da coluna diferencia acento, e a busca nao pode.
      Assert.Equal([acentuado], await AchadosPorAsync($"metalurgica avila {tok}"));
      // Com acento e em maiuscula, e ainda por cima so o comeco do cliente.
      Assert.Equal([acentuado], await AchadosPorAsync("METAL\u00daRGICA"));
      // O outro cliente do mesmo token nao casa com o texto do primeiro.
      Assert.DoesNotContain(outro, await AchadosPorAsync("metalurgica"));
    }
    finally
    {
      await ApagarPedidosAsync(ids);
    }
  }

  [Fact]
  public async Task Status_combina_com_OU()
  {
    var cliente = $"cli-{Unico()}";
    await using var db = NovoContexto();
    var ids = new List<int>();
    try
    {
      var aberto = await NovoPedidoAsync(db, "A-" + Unico(), cliente, "Aberto");
      var emProducao = await NovoPedidoAsync(db, "E-" + Unico(), cliente, "EmProducao");
      var cancelado = await NovoPedidoAsync(db, "C-" + Unico(), cliente, "Cancelado");
      ids.AddRange([aberto, emProducao, cancelado]);

      var (itens, total) = await new PedidoRepository(db).ListarAsync(
          Filtro(busca: cliente, status: ["Aberto", "Cancelado"]), CancellationToken.None);

      Assert.Equal(new[] { aberto, cancelado }.Order(), itens.Select(p => p.Id).Order());
      Assert.Equal(2, total);
    }
    finally
    {
      await ApagarPedidosAsync(ids);
    }
  }

  [Fact]
  public async Task Material_casa_se_algum_no_do_Pedido_tem_o_material()
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "mat");
    try
    {
      var doNo = await arvore.NovoMaterialAsync(db, "Material do no filho");
      var outro = await arvore.NovoMaterialAsync(db, "Material que nenhum no tem");
      var peca = await arvore.NovaPecaAsync(db, 1m);
      var filho = await arvore.NovoItemAsync(db, peca, 1m, 1m);
      await arvore.MaterialNoNoAsync(db, filho, doNo);   // so o FILHO tem; a Peca nao
      var repo = new PedidoRepository(db);

      var achado = await repo.ListarAsync(Filtro(materiais: [doNo]), CancellationToken.None);
      var naoAchado = await repo.ListarAsync(Filtro(materiais: [outro]), CancellationToken.None);
      var ouDosDois = await repo.ListarAsync(Filtro(materiais: [outro, doNo]), CancellationToken.None);

      Assert.Equal([arvore.PedidoId], achado.Itens.Select(p => p.Id));
      Assert.Equal(1, achado.Total);
      Assert.Empty(naoAchado.Itens);
      Assert.Equal(0, naoAchado.Total);
      Assert.Equal([arvore.PedidoId], ouDosDois.Itens.Select(p => p.Id));
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }

  [Fact]
  public async Task Parametros_combinam_com_E()
  {
    var cliente = $"cli-{Unico()}";
    await using var db = NovoContexto();
    var arvores = new List<ArvoreDeTesteNoBanco>();
    try
    {
      // Quatro Pedidos; so o primeiro cumpre os TRES parametros. Cada um dos outros erra um so.
      var alvo = await ArvoreComPedidoAsync(db, "e1", cliente, "EmProducao");
      arvores.Add(alvo);
      var material = await alvo.NovoMaterialAsync(db, "Material do E");
      var erraStatus = await ArvoreComPedidoAsync(db, "e2", cliente, "Aberto");
      var erraMaterial = await ArvoreComPedidoAsync(db, "e3", cliente, "EmProducao");
      var erraBusca = await ArvoreComPedidoAsync(db, "e4", $"outro-{Unico()}", "EmProducao");
      arvores.AddRange([erraStatus, erraMaterial, erraBusca]);
      foreach (var a in new[] { alvo, erraStatus, erraBusca })
        await a.MaterialNoNoAsync(db, await a.NovaPecaAsync(db, 1m), material);
      await erraMaterial.NovaPecaAsync(db, 1m);

      var (itens, total) = await new PedidoRepository(db).ListarAsync(
          Filtro(busca: cliente, status: ["EmProducao"], materiais: [material]), CancellationToken.None);

      Assert.Equal([alvo.PedidoId], itens.Select(p => p.Id));
      Assert.Equal(1, total);
    }
    finally
    {
      await LimparArvoresAsync(arvores);
    }
  }

  [Fact]
  public async Task Total_e_do_filtro_e_a_ordem_e_abertura_decrescente_com_desempate_por_Id()
  {
    var cliente = $"cli-{Unico()}";
    var t1 = new DateTime(2001, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    var t2 = t1.AddHours(1);
    await using var db = NovoContexto();
    var ids = new List<int>();
    try
    {
      var antigo = await NovoPedidoAsync(db, "O-" + Unico(), cliente, dataAbertura: t1);
      var empateA = await NovoPedidoAsync(db, "A-" + Unico(), cliente, dataAbertura: t2);
      var empateB = await NovoPedidoAsync(db, "B-" + Unico(), cliente, dataAbertura: t2);
      ids.AddRange([antigo, empateA, empateB]);
      var repo = new PedidoRepository(db);

      var pagina1 = await repo.ListarAsync(Filtro(busca: cliente, pagina: 1, tamanho: 2), CancellationToken.None);
      var pagina2 = await repo.ListarAsync(Filtro(busca: cliente, pagina: 2, tamanho: 2), CancellationToken.None);

      // Mais recente primeiro; no empate de DataAbertura, o de Id maior (criado depois) primeiro.
      Assert.Equal([empateB, empateA], pagina1.Itens.Select(p => p.Id));
      Assert.Equal([antigo], pagina2.Itens.Select(p => p.Id));
      Assert.Equal(3, pagina1.Total);
      Assert.Equal(3, pagina2.Total);
    }
    finally
    {
      await ApagarPedidosAsync(ids);
    }
  }

  [Fact]
  public async Task Numero_ordena_crescente()
  {
    // As datas sao dadas em ordem CONTRARIA a do numero: a ordem padrao (DataAbertura decrescente)
    // devolveria "-a" por ultimo, entao so a ordem por Numero faz o teste passar.
    var tok = Unico();
    var cliente = $"cli-{tok}";
    var t1 = new DateTime(2001, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    await using var db = NovoContexto();
    var ids = new List<int>();
    try
    {
      var c = await NovoPedidoAsync(db, $"{tok}-c", cliente, dataAbertura: t1.AddHours(3));
      var a = await NovoPedidoAsync(db, $"{tok}-a", cliente, dataAbertura: t1.AddHours(1));
      var b = await NovoPedidoAsync(db, $"{tok}-b", cliente, dataAbertura: t1.AddHours(2));
      ids.AddRange([c, a, b]);

      var (itens, _) = await new PedidoRepository(db).ListarAsync(
          Filtro(busca: cliente, ordem: OrdemDePedidos.Numero), CancellationToken.None);

      Assert.Equal([a, b, c], itens.Select(p => p.Id));
    }
    finally
    {
      await ApagarPedidosAsync(ids);
    }
  }

  [Fact]
  public async Task Cliente_desempata_por_Id_decrescente()
  {
    // Dois Pedidos do mesmo cliente e um de cliente menor, criado por ultimo: o menor vem
    // primeiro, e entre os iguais vem o de maior Id (o criado depois). O de cliente menor tem a
    // data MAIS ANTIGA, para a ordem padrao (DataAbertura decrescente) o por por ultimo.
    var tok = Unico();
    var t1 = new DateTime(2001, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    await using var db = NovoContexto();
    var ids = new List<int>();
    try
    {
      var primeiro = await NovoPedidoAsync(db, $"{tok}-1", $"{tok} Beta", dataAbertura: t1.AddHours(1));
      var segundo = await NovoPedidoAsync(db, $"{tok}-2", $"{tok} Beta", dataAbertura: t1.AddHours(1));
      var menor = await NovoPedidoAsync(db, $"{tok}-3", $"{tok} Alfa", dataAbertura: t1);
      ids.AddRange([primeiro, segundo, menor]);

      var (itens, _) = await new PedidoRepository(db).ListarAsync(
          Filtro(busca: tok, ordem: OrdemDePedidos.Cliente), CancellationToken.None);

      Assert.Equal([menor, segundo, primeiro], itens.Select(p => p.Id));
    }
    finally
    {
      await ApagarPedidosAsync(ids);
    }
  }

  [Fact]
  public async Task Mais_urgentes_deixa_encerrados_de_fora_ordena_por_prazo_e_para_no_limite()
  {
    var ids = new List<int>();
    await using var db = NovoContexto();
    try
    {
      // `ListarMaisUrgentesAsync` nao tem como ser escopado: le a tabela inteira. Por isso a asercao
      // vale com QUALQUER linha de terceiros no banco (inclusive sobra de execucao interrompida, ou um
      // Pedido gravado sem data, que vale 0001-01-01) e so compara as linhas deste teste com o que a
      // consulta devolveu.
      var abertura = new DateTime(2001, 1, 1, 8, 0, 0, DateTimeKind.Utc);
      var chaves = new Dictionary<int, (DateOnly Prazo, DateTime Abertura)>();
      async Task<int> CriarAsync(string status, DateOnly prazo)
      {
        var id = await NovoPedidoAsync(db, "U-" + Unico(), "Cliente", status, abertura, prazo);
        ids.Add(id);   // entra na limpeza logo apos criar: nao ha janela de vazamento
        chaves[id] = (prazo, abertura);
        return id;
      }

      var abertos = new List<int>();
      var statusDosAbertos = new[] { "Aberto", "EmProducao", "AguardandoExpedicao", "Aberto", "EmProducao", "Aberto" };
      for (var i = 0; i < statusDosAbertos.Length; i++)
        abertos.Add(await CriarAsync(statusDosAbertos[i], new DateOnly(1990, 1, 1 + i)));
      // Encerrados com o prazo MAIS ANTIGO de todos: entrariam primeiro se nao fossem excluidos.
      var concluido = await CriarAsync("Concluido", new DateOnly(1989, 1, 1));
      var cancelado = await CriarAsync("Cancelado", new DateOnly(1989, 1, 2));

      var achados = await new PedidoRepository(db).ListarMaisUrgentesAsync(
          ["Concluido", "Cancelado"], 5, CancellationToken.None);

      // Para no limite: exatamente cinco, com seis candidatos nossos.
      Assert.Equal(5, achados.Count);
      // Prazo crescente, depois abertura, depois Id.
      Assert.Equal(
          achados.OrderBy(p => p.DataEntrega).ThenBy(p => p.DataAbertura).ThenBy(p => p.Id).Select(p => p.Id),
          achados.Select(p => p.Id));
      Assert.DoesNotContain(achados, p => p.Status is "Concluido" or "Cancelado");
      Assert.DoesNotContain(achados, p => p.Id == concluido || p.Id == cancelado);
      // Nenhuma linha nossa que devia entrar ficou de fora: as nossas ausentes rankeiam depois da quinta.
      var quinto = achados[^1];
      foreach (var id in abertos.Where(id => achados.All(p => p.Id != id)))
        Assert.True(
            (chaves[id].Prazo, chaves[id].Abertura, id).CompareTo((quinto.DataEntrega, quinto.DataAbertura, quinto.Id)) >= 0,
            $"Pedido {id} (aberto, prazo {chaves[id].Prazo:O}) devia ter entrado antes do quinto ({quinto.Id}, {quinto.DataEntrega:O}).");
    }
    finally
    {
      await ApagarPedidosAsync(ids);
    }
  }

  [Fact]
  public async Task Entrega_poe_os_abertos_por_prazo_e_depois_os_encerrados_do_prazo_mais_recente()
  {
    // Cada mutacao da ordem troca o resultado: o encerrado de prazo MAIS ANTIGO de todos (ordenar so pela
    // data o poria no topo), dois encerrados (prazo crescente entre eles inverteria o par) e dois abertos
    // de mesmo prazo (o desempate por abertura decide). Escopado pela busca do cliente unico do teste.
    var cliente = $"cli-{Unico()}";
    var abertura = new DateTime(2001, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    await using var db = NovoContexto();
    var ids = new List<int>();
    try
    {
      var concluidoAntigo = await NovoPedidoAsync(db, "E1-" + Unico(), cliente, "Concluido", abertura, new DateOnly(2020, 1, 1));
      var canceladoRecente = await NovoPedidoAsync(db, "E2-" + Unico(), cliente, "Cancelado", abertura, new DateOnly(2020, 6, 1));
      var abertoTarde = await NovoPedidoAsync(db, "A1-" + Unico(), cliente, "Aberto", abertura, new DateOnly(2026, 12, 1));
      var empateAbertoDepois = await NovoPedidoAsync(db, "A2-" + Unico(), cliente, "EmProducao", abertura.AddHours(2), new DateOnly(2026, 11, 1));
      var empateAbertoAntes = await NovoPedidoAsync(db, "A3-" + Unico(), cliente, "Aberto", abertura.AddHours(1), new DateOnly(2026, 11, 1));
      ids.AddRange([concluidoAntigo, canceladoRecente, abertoTarde, empateAbertoDepois, empateAbertoAntes]);

      var (itens, _) = await new PedidoRepository(db).ListarAsync(
          Filtro(busca: cliente, ordem: OrdemDePedidos.Entrega), CancellationToken.None);

      Assert.Equal(
          [empateAbertoAntes, empateAbertoDepois, abertoTarde, canceladoRecente, concluidoAntigo],
          itens.Select(p => p.Id));
    }
    finally
    {
      await ApagarPedidosAsync(ids);
    }
  }

  [Fact]
  public async Task Entrega_desempata_abertos_por_Id_crescente_e_encerrados_por_Id_decrescente()
  {
    // Mesmo prazo e mesma abertura em cada par: so o Id decide, e em direcoes opostas.
    var cliente = $"cli-{Unico()}";
    var abertura = new DateTime(2001, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    var prazo = new DateOnly(2026, 11, 1);
    await using var db = NovoContexto();
    var ids = new List<int>();
    try
    {
      var aberto1 = await NovoPedidoAsync(db, "A1-" + Unico(), cliente, "Aberto", abertura, prazo);
      var aberto2 = await NovoPedidoAsync(db, "A2-" + Unico(), cliente, "Aberto", abertura, prazo);
      var encerrado1 = await NovoPedidoAsync(db, "E1-" + Unico(), cliente, "Concluido", abertura, prazo);
      var encerrado2 = await NovoPedidoAsync(db, "E2-" + Unico(), cliente, "Concluido", abertura, prazo);
      ids.AddRange([aberto1, aberto2, encerrado1, encerrado2]);

      var (itens, _) = await new PedidoRepository(db).ListarAsync(
          Filtro(busca: cliente, ordem: OrdemDePedidos.Entrega), CancellationToken.None);

      Assert.Equal([aberto1, aberto2, encerrado2, encerrado1], itens.Select(p => p.Id));
    }
    finally
    {
      await ApagarPedidosAsync(ids);
    }
  }

  [Fact]
  public async Task Materiais_em_uso_trazem_o_que_aparece_em_algum_no_e_nao_o_que_nao_aparece()
  {
    var tok = Unico();
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "uso");
    try
    {
      var b = await arvore.NovoMaterialAsync(db, $"b-{tok}");
      var a = await arvore.NovoMaterialAsync(db, $"a-{tok}");
      var solto = await arvore.NovoMaterialAsync(db, $"solto-{tok}");
      var peca = await arvore.NovaPecaAsync(db, 1m);
      await arvore.MaterialNoNoAsync(db, peca, b);
      await arvore.MaterialNoNoAsync(db, peca, a);

      var ids = (await new PedidoRepository(db).ListarMateriaisEmUsoAsync(CancellationToken.None))
          .Select(m => m.Id).ToList();

      Assert.Contains(a, ids);
      Assert.Contains(b, ids);
      Assert.DoesNotContain(solto, ids);
      // Ordem por descricao: "a-..." antes de "b-...", embora `b` tenha sido criado primeiro.
      Assert.True(ids.IndexOf(a) < ids.IndexOf(b));
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }
}

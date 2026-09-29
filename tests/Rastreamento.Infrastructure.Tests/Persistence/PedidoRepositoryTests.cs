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
      string? busca = null, string[]? status = null, int[]? materiais = null, int pagina = 1, int tamanho = 100) =>
      new(busca, status ?? [], materiais ?? [], pagina, tamanho);

  /// <summary>Pedido solto (sem Agrupamento); a limpeza dele e <see cref="ApagarPedidosAsync"/>.</summary>
  private static async Task<int> NovoPedidoAsync(
      RastreamentoDbContext db, string numero, string cliente, string status = "Aberto", DateTime? dataAbertura = null)
  {
    var autor = (await db.Usuarios.AsNoTracking().SingleAsync(u => u.NomeUsuario == "admin")).Id;
    var pedido = new Pedido
    {
      Numero = numero, Cliente = cliente, Tipo = "Fabricacao", Status = status,
      DataAbertura = dataAbertura ?? DateTime.UtcNow, CriadoPorUsuarioId = autor,
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
  public async Task Mais_antigos_deixa_encerrados_de_fora_e_para_no_limite()
  {
    var ids = new List<int>();
    await using var db = NovoContexto();
    try
    {
      // `ListarMaisAntigosAsync` nao tem como ser escopado: le a tabela inteira. Por isso a asercao
      // vale com QUALQUER linha de terceiros no banco (inclusive sobra de execucao interrompida) e
      // so compara as linhas deste teste com o que a consulta devolveu.
      var datas = new Dictionary<int, DateTime>();
      async Task<int> CriarAsync(string status, DateTime data)
      {
        var id = await NovoPedidoAsync(db, "M-" + Unico(), "Cliente", status, data);
        ids.Add(id);   // entra na limpeza logo apos criar: nao ha janela de vazamento
        datas[id] = data;
        return id;
      }

      var abertos = new List<int>();
      var statusDosAbertos = new[] { "Aberto", "EmProducao", "AguardandoExpedicao", "Aberto", "EmProducao", "Aberto" };
      for (var i = 0; i < statusDosAbertos.Length; i++)
        abertos.Add(await CriarAsync(statusDosAbertos[i], new DateTime(1990, 1, 1 + i, 8, 0, 0, DateTimeKind.Utc)));
      var concluido = await CriarAsync("Concluido", new DateTime(1989, 1, 1, 8, 0, 0, DateTimeKind.Utc));
      var cancelado = await CriarAsync("Cancelado", new DateTime(1989, 1, 2, 8, 0, 0, DateTimeKind.Utc));

      var achados = await new PedidoRepository(db).ListarMaisAntigosAsync(
          ["Concluido", "Cancelado"], 5, CancellationToken.None);

      // Para no limite: exatamente cinco, com seis candidatos nossos.
      Assert.Equal(5, achados.Count);
      // Do mais antigo ao mais novo, com desempate por Id.
      Assert.Equal(
          achados.OrderBy(p => p.DataAbertura).ThenBy(p => p.Id).Select(p => p.Id),
          achados.Select(p => p.Id));
      // Nenhum encerrado, nem os nossos (mais antigos que todos os abertos: entrariam se nao fossem excluidos).
      Assert.DoesNotContain(achados, p => p.Status is "Concluido" or "Cancelado");
      Assert.DoesNotContain(achados, p => p.Id == concluido || p.Id == cancelado);
      // Nenhuma linha nossa que devia entrar ficou de fora: as nossas ausentes rankeiam depois do quinto.
      var quinto = achados[^1];
      foreach (var id in abertos.Where(id => achados.All(p => p.Id != id)))
        Assert.True(
            (datas[id], id).CompareTo((quinto.DataAbertura, quinto.Id)) >= 0,
            $"Pedido {id} (aberto, {datas[id]:O}) devia ter entrado antes do quinto ({quinto.Id}, {quinto.DataAbertura:O}).");
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

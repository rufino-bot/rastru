using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;
using Xunit;
using static Rastreamento.Api.Tests.CenarioDaFase3NaApi;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Abrir o Pedido de Retrabalho pelas tres portas (secoes 4.2, 4.3 e 4.7 da spec da Fase 5B-2) no cenario da Fase 3:
/// Peca A (10, copiada do catalogo, sem descricao propria, "Chassi da Fase 3"; Solda -> Pintura), B (20, razao 2,
/// "Suporte"; Corte) e C (10, razao 1, "Calço"; Dobra). A reclamacao usa uma Peca nova expedida no lugar de A. Toda
/// assercao escopada no Pedido do cenario e nos Retrabalhos que o tem como origem (que o cenario apaga junto).
/// </summary>
public class RetrabalhoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private const string SemAtraso = "2099-12-31";

  private readonly WebApplicationFactory<Program> _factory;

  public RetrabalhoEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  private static Task<HttpResponseMessage> AbrirAsync(CenarioDaFase3NaApi c, UsuarioDeTeste quem, object corpo) =>
      c.Como(quem).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/retrabalhos", corpo);

  private static async Task<string> NumeroDoOriginalAsync(CenarioDaFase3NaApi c) =>
      (await CorpoAsync(await c.Como(c.Pcp).GetAsync($"/api/pedidos/{c.PedidoId}"))).GetProperty("numero").GetString()!;

  private static object DaBaixa(int perdaId, string numero, string? motivo = "Perda") =>
      new { perdaId, numero, dataEntrega = SemAtraso, motivoRetrabalho = motivo };

  private static object DaReclamacao(int reclamacaoId, string numero, string? destino, int? setorId) =>
      new { reclamacaoId, destino, setorId, numero, dataEntrega = SemAtraso, motivoRetrabalho = "SolicitacaoCliente" };

  private static object Avulso(int estruturaItemId, decimal quantidade, string numero, string? motivo = "ErroInterno") =>
      new { estruturaItemId, quantidade, numero, dataEntrega = SemAtraso, motivoRetrabalho = motivo };

  /// <summary>Uma baixa de "a iniciar" do no; devolve o Id dela.</summary>
  private static async Task<int> DarBaixaAsync(CenarioDaFase3NaApi c, int no, string motivo, decimal quantidade, bool incluirTopo)
  {
    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{no}/baixas", new
    {
      motivo, observacao = (string?)null, tudo = false,
      origens = new[] { new { posicao = "AIniciar", setorId = (int?)null, ordem = (int?)null, quantidade } },
      incluirTopo,
    });
    await Garantir(resposta);
    return (await CorpoAsync(resposta)).GetProperty("id").GetInt32();
  }

  /// <summary>
  /// A Peca A sai e uma Peca nova, copiada do catalogo (sem descricao propria), com um material planejado de 2 por
  /// unidade e o Roteiro so no Corte, vai inteira ao local de expedicao e e expedida. Devolve o Id dela.
  /// </summary>
  private static async Task<(int Peca, int Material)> PecaExpedidaAsync(CenarioDaFase3NaApi c, decimal quantidade)
  {
    var pcp = c.Como(c.Pcp);
    await Garantir(await pcp.DeleteAsync($"/api/estrutura/{c.A}"));
    var criada = await pcp.PostAsJsonAsync($"/api/agrupamentos/{c.AgrupamentoId}/estrutura",
        new { componenteId = c.ComponenteId, quantidade, requerRelatorioDimensional = false });
    await Garantir(criada);
    var peca = (await CorpoAsync(criada)).GetProperty("id").GetInt32();
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{peca}/roteiro", new { passos = new[] { c.Corte } }));
    var material = await c.CriarMaterialAsync("RT");
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{peca}/materiais",
        new { linhas = new[] { new { materialId = material, quantidade = 2m * quantidade } } }));

    var operador = c.Como(c.Operador);
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{peca}/inicios", new { setorId = c.Corte, quantidade }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{peca}/terminos", new { setorId = c.Corte, ordem = 1, quantidade }));
    await Garantir(await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new
    {
      itens = new[] { new { estruturaItemId = peca, origem = new { posicao = "AguardandoColeta", setorId = c.Corte, ordem = (int?)1 }, destinoSetorId = (int?)null, quantidade } },
    }));
    await Garantir(await c.Como(c.Qualidade).PostAsJsonAsync("/api/remessas",
        new { itens = new[] { new { estruturaItemId = peca, quantidade } } }));
    return (peca, material);
  }

  private static async Task<int> ReclamarAsync(CenarioDaFase3NaApi c, int peca, decimal quantidade)
  {
    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/reclamacoes",
        new { estruturaItemId = peca, quantidade, descricao = "Voltou com rebarba." });
    await Garantir(resposta);
    return (await CorpoAsync(resposta)).GetProperty("id").GetInt32();
  }

  private static async Task<(HttpStatusCode Status, string? Erro, string? Mensagem)> RecusaAsync(HttpResponseMessage resposta)
  {
    var corpo = await CorpoAsync(resposta);
    return (resposta.StatusCode,
        corpo.TryGetProperty("erro", out var e) ? e.GetString() : null,
        corpo.TryGetProperty("mensagem", out var m) ? m.GetString() : null);
  }

  private async Task<int> RetrabalhosDoOriginalAsync(CenarioDaFase3NaApi c)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    return await db.Pedidos.AsNoTracking().CountAsync(p => p.PedidoOrigemId == c.PedidoId);
  }

  [Fact]
  public async Task Abrir_da_baixa_copia_a_Peca_para_as_unidades_perdidas()
  {
    await using var c = await CriarAsync(_factory);
    var material = await c.CriarMaterialAsync("B");
    await Garantir(await c.Como(c.Pcp).PutAsJsonAsync($"/api/estrutura/{c.B}/materiais",
        new { linhas = new[] { new { materialId = material, quantidade = 10m } } }));
    // Sem 2 de B, A so monta 9 das 10: a baixa leva 1 de A (incluirTopo), e por C6 1 de C.
    var perdaId = await DarBaixaAsync(c, c.B, "PerdaArmazem", 2m, incluirTopo: true);
    var numero = $"{await NumeroDoOriginalAsync(c)}-R1";

    var resposta = await AbrirAsync(c, c.Qualidade, DaBaixa(perdaId, numero));

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var corpo = await CorpoAsync(resposta);
    var retrabalhoId = corpo.GetProperty("id").GetInt32();
    Assert.Equal((numero, "Retrabalho", "Aberto", "Cliente F3", SemAtraso),
        (corpo.GetProperty("numero").GetString(), corpo.GetProperty("tipo").GetString(), corpo.GetProperty("status").GetString(),
         corpo.GetProperty("cliente").GetString(), corpo.GetProperty("dataEntrega").GetString()));

    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var retrabalho = await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == retrabalhoId);
    Assert.Equal(("Retrabalho", (int?)c.PedidoId, "Perda", "Aberto", c.Qualidade.Id),
        (retrabalho.Tipo, retrabalho.PedidoOrigemId, retrabalho.MotivoRetrabalho, retrabalho.Status, retrabalho.CriadoPorUsuarioId));

    // Um Agrupamento, com o nome e o Tipo do da Peca original.
    var agrupamento = await db.Agrupamentos.AsNoTracking().SingleAsync(a => a.PedidoId == retrabalhoId);
    Assert.Equal(("AG-01", "Avulso"), (agrupamento.Codigo, agrupamento.Tipo));

    // A arvore escalada para 1 unidade da Peca, e cada no guarda de que no veio (C14).
    var nos = await db.Estruturas.AsNoTracking().Where(e => e.AgrupamentoId == agrupamento.Id).ToListAsync();
    Assert.All(nos, n => Assert.NotNull(n.OrigemEstruturaItemId));
    var porOrigem = nos.ToDictionary(n => n.OrigemEstruturaItemId!.Value);
    Assert.Equal(new[] { c.A, c.B, c.C }.Order(), porOrigem.Keys.Order());
    var (a, b, cc) = (porOrigem[c.A], porOrigem[c.B], porOrigem[c.C]);
    Assert.Equal(((int?)c.ComponenteId, (string?)null, 1m, (int?)null, (decimal?)null), (a.ComponenteId, a.Descricao, a.Quantidade, a.EstruturaPaiId, a.QuantidadePorPai));
    Assert.Equal(("Suporte", 2m, (int?)a.Id, (decimal?)2m), (b.Descricao, b.Quantidade, b.EstruturaPaiId, b.QuantidadePorPai));
    Assert.Equal(("Calço", 1m, (int?)a.Id, (decimal?)1m), (cc.Descricao, cc.Quantidade, cc.EstruturaPaiId, cc.QuantidadePorPai));

    // Os Roteiros copiados como estao, e o material reescalado pela razao do no.
    var roteiros = await db.EstruturaRoteiros.AsNoTracking().Where(r => nos.Select(n => n.Id).Contains(r.EstruturaItemId)).ToListAsync();
    Assert.Equal(new[] { c.Solda, c.Pintura }, roteiros.Where(r => r.EstruturaItemId == a.Id).OrderBy(r => r.Ordem).Select(r => r.SetorId));
    Assert.Equal(new[] { c.Corte }, roteiros.Where(r => r.EstruturaItemId == b.Id).Select(r => r.SetorId));
    Assert.Equal(new[] { c.Dobra }, roteiros.Where(r => r.EstruturaItemId == cc.Id).Select(r => r.SetorId));
    var materiais = await db.EstruturaMateriais.AsNoTracking().Where(m => nos.Select(n => n.Id).Contains(m.EstruturaItemId)).ToListAsync();
    var doB = Assert.Single(materiais);
    Assert.Equal((b.Id, material, 1m), (doB.EstruturaItemId, doB.MaterialId, doB.Quantidade));

    // Tudo nasce a iniciar (regra 28): nenhuma linha no livro do Retrabalho.
    Assert.False(await db.Movimentacoes.AsNoTracking().AnyAsync(m => nos.Select(n => n.Id).Contains(m.EstruturaItemId)));

    // O vinculo e por baixa (C2).
    Assert.Equal(retrabalhoId, (await db.Perdas.AsNoTracking().SingleAsync(p => p.Id == perdaId)).PedidoRetrabalhoId);
  }

  [Fact]
  public async Task Abrir_da_baixa_sem_Peca_e_409_SemPecaARepor()
  {
    await using var c = await CriarAsync(_factory);
    // Sem incluirTopo, a baixa so leva B: nao ha unidade de Peca a repor (C1).
    var perdaId = await DarBaixaAsync(c, c.B, "PerdaArmazem", 2m, incluirTopo: false);

    var (status, erro, _) = await RecusaAsync(await AbrirAsync(c, c.Qualidade, DaBaixa(perdaId, $"{await NumeroDoOriginalAsync(c)}-R1")));

    Assert.Equal((HttpStatusCode.Conflict, "SemPecaARepor"), (status, erro));
    Assert.Equal(0, await RetrabalhosDoOriginalAsync(c));
  }

  [Fact]
  public async Task Abrir_da_baixa_de_Descarte_e_409_SemPecaARepor()
  {
    await using var c = await CriarAsync(_factory);
    var perdaId = await DarBaixaAsync(c, c.A, "Descarte", 1m, incluirTopo: false);

    var (status, erro, _) = await RecusaAsync(await AbrirAsync(c, c.Qualidade, DaBaixa(perdaId, $"{await NumeroDoOriginalAsync(c)}-R1")));

    Assert.Equal((HttpStatusCode.Conflict, "SemPecaARepor"), (status, erro));
    Assert.Equal(0, await RetrabalhosDoOriginalAsync(c));
  }

  [Fact]
  public async Task Abrir_da_baixa_estornada_e_409_SemPecaARepor()
  {
    await using var c = await CriarAsync(_factory);
    var perdaId = await DarBaixaAsync(c, c.A, "PerdaArmazem", 1m, incluirTopo: false);
    await Garantir(await c.Como(c.Qualidade).PostAsync($"/api/baixas/{perdaId}/estorno", null));

    var (status, erro, _) = await RecusaAsync(await AbrirAsync(c, c.Qualidade, DaBaixa(perdaId, $"{await NumeroDoOriginalAsync(c)}-R1")));

    Assert.Equal((HttpStatusCode.Conflict, "SemPecaARepor"), (status, erro));
    Assert.Equal(0, await RetrabalhosDoOriginalAsync(c));
  }

  [Fact]
  public async Task Abrir_duas_vezes_da_mesma_baixa_e_409_JaReposto()
  {
    await using var c = await CriarAsync(_factory);
    var perdaId = await DarBaixaAsync(c, c.A, "MortaEmProcesso", 2m, incluirTopo: false);
    var numero = await NumeroDoOriginalAsync(c);
    await Garantir(await AbrirAsync(c, c.Qualidade, DaBaixa(perdaId, $"{numero}-R1", "ReprovacaoDimensional")));

    var (status, erro, mensagem) = await RecusaAsync(await AbrirAsync(c, c.Pcp, DaBaixa(perdaId, $"{numero}-R2")));

    Assert.Equal((HttpStatusCode.Conflict, "JaReposto"), (status, erro));
    Assert.Contains($"{numero}-R1", mensagem);
    Assert.Equal(1, await RetrabalhosDoOriginalAsync(c));
  }

  [Fact]
  public async Task Abrir_da_mesma_baixa_em_paralelo_da_um_201_e_um_409_JaReposto()
  {
    // Decisao P5 do plano de backend da Fase 5B-2: a trava do Pedido original e a releitura da baixa depois dela
    // serializam as duas aberturas; a segunda ve a primeira e nao abre um segundo Retrabalho.
    await using var c = await CriarAsync(_factory);
    var perdaId = await DarBaixaAsync(c, c.A, "PerdaArmazem", 2m, incluirTopo: false);
    var numero = await NumeroDoOriginalAsync(c);

    var respostas = await Task.WhenAll(
        AbrirAsync(c, c.Qualidade, DaBaixa(perdaId, $"{numero}-R1")),
        AbrirAsync(c, c.Pcp, DaBaixa(perdaId, $"{numero}-R2")));

    var aberta = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Created);
    var recusada = Assert.Single(respostas, r => r.StatusCode != HttpStatusCode.Created);
    var (status, erro, _) = await RecusaAsync(recusada);
    Assert.Equal((HttpStatusCode.Conflict, "JaReposto"), (status, erro));
    Assert.Equal(1, await RetrabalhosDoOriginalAsync(c));
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    Assert.Equal((await CorpoAsync(aberta)).GetProperty("id").GetInt32(),
        (await db.Perdas.AsNoTracking().SingleAsync(p => p.Id == perdaId)).PedidoRetrabalhoId);
  }

  [Fact]
  public async Task Abrir_da_reclamacao_para_consertar()
  {
    await using var c = await CriarAsync(_factory);
    var (peca, _) = await PecaExpedidaAsync(c, 2m);
    var reclamacaoId = await ReclamarAsync(c, peca, 1m);

    var resposta = await AbrirAsync(c, c.Qualidade, DaReclamacao(reclamacaoId, $"{await NumeroDoOriginalAsync(c)}-R1", "Consertar", c.Dobra));

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var corpo = await CorpoAsync(resposta);
    var retrabalhoId = corpo.GetProperty("id").GetInt32();
    // A Devolucao conta como inicio (C13): o Retrabalho de consertar ja nasce em producao.
    Assert.Equal("EmProducao", corpo.GetProperty("status").GetString());

    int copia;
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      var retrabalho = await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == retrabalhoId);
      Assert.Equal(("SolicitacaoCliente", "EmProducao"), (retrabalho.MotivoRetrabalho, retrabalho.Status));
      // So a Peca: sem filhos e sem materiais, com o Roteiro copiado (secao 4.7).
      var no = await db.Estruturas.AsNoTracking().SingleAsync(e => db.Agrupamentos.Any(a => a.Id == e.AgrupamentoId && a.PedidoId == retrabalhoId));
      copia = no.Id;
      Assert.Equal(((int?)peca, 1m, (int?)null), (no.OrigemEstruturaItemId, no.Quantidade, no.EstruturaPaiId));
      Assert.False(await db.EstruturaMateriais.AsNoTracking().AnyAsync(m => m.EstruturaItemId == copia));
      Assert.Equal(new[] { c.Corte }, await db.EstruturaRoteiros.AsNoTracking().Where(r => r.EstruturaItemId == copia).Select(r => r.SetorId).ToListAsync());

      var devolucao = await db.Movimentacoes.AsNoTracking().SingleAsync(m => m.EstruturaItemId == copia);
      Assert.Equal(("Devolucao", 1m, "AIniciar", "AguardandoConserto", (int?)c.Dobra, (int?)reclamacaoId),
          (devolucao.Tipo, devolucao.Quantidade, devolucao.OrigemPosicao, devolucao.DestinoPosicao, devolucao.DestinoSetorId, devolucao.ReclamacaoId));
      Assert.Equal(retrabalhoId, (await db.Reclamacoes.AsNoTracking().SingleAsync(r => r.Id == reclamacaoId)).PedidoRetrabalhoId);
    }

    // Dali, o conserto da 5A: o Movimentador o ve nas Tarefas, leva ao Setor, e ele aparece na Fila.
    var movimentador = c.Como(c.Movimentador);
    var ida = Assert.Single(
        (await CorpoAsync(await movimentador.GetAsync("/api/tarefas"))).GetProperty("consertos").EnumerateArray(),
        t => t.GetProperty("no").GetProperty("id").GetInt32() == copia);
    Assert.Equal(("AguardandoConserto", c.Dobra, "EmConserto", 1m),
        (ida.GetProperty("origem").GetProperty("posicao").GetString(), ida.GetProperty("origem").GetProperty("setorId").GetInt32(),
         ida.GetProperty("destino").GetProperty("posicao").GetString(), ida.GetProperty("quantidade").GetDecimal()));
    await Garantir(await movimentador.PostAsJsonAsync("/api/entregas", new
    {
      itens = new[] { new { estruturaItemId = copia, origem = new { posicao = "AguardandoConserto", setorId = c.Dobra, ordem = (int?)null }, destinoSetorId = (int?)null, quantidade = 1m } },
    }));
    var naFila = Assert.Single(
        (await CorpoAsync(await c.Como(c.Operador).GetAsync($"/api/setores/{c.Dobra}/fila"))).GetProperty("conserto").EnumerateArray(),
        l => l.GetProperty("no").GetProperty("id").GetInt32() == copia);
    Assert.Equal(("EmConserto", 1m), (naFila.GetProperty("posicao").GetString(), naFila.GetProperty("quantidade").GetDecimal()));
  }

  [Fact]
  public async Task Estornar_a_remessa_de_reclamacao_com_Retrabalho_diz_que_ela_nao_se_estorna_mais()
  {
    await using var c = await CriarAsync(_factory);
    var (peca, _) = await PecaExpedidaAsync(c, 2m);
    var reclamacaoId = await ReclamarAsync(c, peca, 1m);
    var numero = $"{await NumeroDoOriginalAsync(c)}-R1";
    await Garantir(await AbrirAsync(c, c.Pcp, DaReclamacao(reclamacaoId, numero, "Refazer", null)));
    int remessaId;
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      remessaId = await db.Movimentacoes.AsNoTracking()
          .Where(m => m.EstruturaItemId == peca && m.RemessaId != null).Select(m => m.RemessaId!.Value).FirstAsync();
    }

    var recusada = await c.Como(c.Qualidade).PostAsync($"/api/remessas/{remessaId}/estorno", null);

    var (status, erro, mensagem) = await RecusaAsync(recusada);
    Assert.Equal((HttpStatusCode.Conflict, "AbaixoDoReclamado"), (status, erro));
    Assert.Contains($"A reclamação já tem o Retrabalho {numero}: a remessa não se estorna mais.", mensagem);
    Assert.DoesNotContain("Estorne antes a reclamação", mensagem);
  }

  [Fact]
  public async Task Abrir_da_reclamacao_para_refazer()
  {
    await using var c = await CriarAsync(_factory);
    var (peca, material) = await PecaExpedidaAsync(c, 2m);
    var reclamacaoId = await ReclamarAsync(c, peca, 1m);

    var resposta = await AbrirAsync(c, c.Pcp, DaReclamacao(reclamacaoId, $"{await NumeroDoOriginalAsync(c)}-R1", "Refazer", null));

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var retrabalhoId = (await CorpoAsync(resposta)).GetProperty("id").GetInt32();
    Assert.Equal("Aberto", (await CorpoAsync(resposta)).GetProperty("status").GetString());
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    // A copia inteira (com o material reescalado), toda a iniciar: nenhuma linha no livro.
    var no = await db.Estruturas.AsNoTracking().SingleAsync(e => db.Agrupamentos.Any(a => a.Id == e.AgrupamentoId && a.PedidoId == retrabalhoId));
    Assert.Equal(((int?)peca, 1m), (no.OrigemEstruturaItemId, no.Quantidade));
    var planejado = await db.EstruturaMateriais.AsNoTracking().SingleAsync(m => m.EstruturaItemId == no.Id);
    Assert.Equal((material, 2m), (planejado.MaterialId, planejado.Quantidade));
    Assert.False(await db.Movimentacoes.AsNoTracking().AnyAsync(m => m.EstruturaItemId == no.Id));
    Assert.Equal("Aberto", (await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == retrabalhoId)).Status);
    Assert.Equal(retrabalhoId, (await db.Reclamacoes.AsNoTracking().SingleAsync(r => r.Id == reclamacaoId)).PedidoRetrabalhoId);
  }

  [Fact]
  public async Task Consertar_sem_setor_e_400_DestinoInvalido()
  {
    await using var c = await CriarAsync(_factory);
    var (peca, _) = await PecaExpedidaAsync(c, 1m);
    var reclamacaoId = await ReclamarAsync(c, peca, 1m);
    var numero = await NumeroDoOriginalAsync(c);

    var semSetor = await RecusaAsync(await AbrirAsync(c, c.Qualidade, DaReclamacao(reclamacaoId, $"{numero}-R1", "Consertar", null)));
    var refazerComSetor = await RecusaAsync(await AbrirAsync(c, c.Qualidade, DaReclamacao(reclamacaoId, $"{numero}-R1", "Refazer", c.Dobra)));

    Assert.Equal((HttpStatusCode.BadRequest, "DestinoInvalido"), (semSetor.Status, semSetor.Erro));
    Assert.Equal((HttpStatusCode.BadRequest, "DestinoInvalido"), (refazerComSetor.Status, refazerComSetor.Erro));
    Assert.Equal(0, await RetrabalhosDoOriginalAsync(c));
  }

  [Fact]
  public async Task Reclamacao_sem_destino_e_400_DestinoInvalido()
  {
    await using var c = await CriarAsync(_factory);
    var (peca, _) = await PecaExpedidaAsync(c, 1m);
    var reclamacaoId = await ReclamarAsync(c, peca, 1m);
    var numero = await NumeroDoOriginalAsync(c);

    foreach (var destino in new[] { null, "Jogar fora" })
    {
      var (status, erro, _) = await RecusaAsync(await AbrirAsync(c, c.Qualidade, DaReclamacao(reclamacaoId, $"{numero}-R1", destino, null)));
      Assert.Equal((HttpStatusCode.BadRequest, "DestinoInvalido"), (status, erro));
    }
    Assert.Equal(0, await RetrabalhosDoOriginalAsync(c));
  }

  [Fact]
  public async Task Setor_inativo_e_400_SetorInvalido()
  {
    await using var c = await CriarAsync(_factory);
    var (peca, _) = await PecaExpedidaAsync(c, 1m);
    var reclamacaoId = await ReclamarAsync(c, peca, 1m);
    var numero = await NumeroDoOriginalAsync(c);
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      await db.Setores.Where(s => s.Id == c.Pintura).ExecuteUpdateAsync(s => s.SetProperty(x => x.Ativo, false));
    }

    foreach (var setor in new[] { c.Pintura, 2000000000 })
    {
      var (status, erro, _) = await RecusaAsync(await AbrirAsync(c, c.Qualidade, DaReclamacao(reclamacaoId, $"{numero}-R1", "Consertar", setor)));
      Assert.Equal((HttpStatusCode.BadRequest, "SetorInvalido"), (status, erro));
    }
    Assert.Equal(0, await RetrabalhosDoOriginalAsync(c));
  }

  [Fact]
  public async Task Abrir_avulso_com_motivo_ErroInterno()
  {
    await using var c = await CriarAsync(_factory);

    var resposta = await AbrirAsync(c, c.Pcp, Avulso(c.A, 3m, $"{await NumeroDoOriginalAsync(c)}-R1"));

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var retrabalhoId = (await CorpoAsync(resposta)).GetProperty("id").GetInt32();
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    Assert.Equal("ErroInterno", (await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == retrabalhoId)).MotivoRetrabalho);
    var nos = await db.Estruturas.AsNoTracking().Where(e => db.Agrupamentos.Any(a => a.Id == e.AgrupamentoId && a.PedidoId == retrabalhoId)).ToListAsync();
    Assert.Equal(new[] { (c.A, 3m), (c.B, 6m), (c.C, 3m) }.OrderBy(x => x.Item1),
        nos.Select(n => (n.OrigemEstruturaItemId!.Value, n.Quantidade)).OrderBy(x => x.Item1));
    // Sem vinculo: nenhuma baixa nem reclamacao aponta o Retrabalho.
    Assert.False(await db.Perdas.AsNoTracking().AnyAsync(p => p.PedidoRetrabalhoId == retrabalhoId));
    Assert.False(await db.Reclamacoes.AsNoTracking().AnyAsync(r => r.PedidoRetrabalhoId == retrabalhoId));
  }

  [Fact]
  public async Task Excluir_no_do_original_que_tem_copia_em_Retrabalho_e_409_NoComRetrabalho_e_nada_e_apagado()
  {
    await using var c = await CriarAsync(_factory);
    await Garantir(await AbrirAsync(c, c.Pcp, Avulso(c.A, 3m, $"{await NumeroDoOriginalAsync(c)}-R1")));

    var resposta = await c.Como(c.Pcp).DeleteAsync($"/api/estrutura/{c.A}");

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var texto = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal("NoComRetrabalho", texto.GetProperty("erro").GetString());
    Assert.False(string.IsNullOrWhiteSpace(texto.GetProperty("mensagem").GetString()));
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    Assert.Equal(3, await db.Estruturas.AsNoTracking().CountAsync(e => e.Id == c.A || e.Id == c.B || e.Id == c.C));
  }

  [Fact]
  public async Task Avulso_com_Item_e_400_NaoEhPeca()
  {
    await using var c = await CriarAsync(_factory);

    var (status, erro, mensagem) = await RecusaAsync(await AbrirAsync(c, c.Pcp, Avulso(c.B, 1m, $"{await NumeroDoOriginalAsync(c)}-R1")));

    Assert.Equal((HttpStatusCode.BadRequest, "NaoEhPeca"), (status, erro));
    Assert.Contains("Suporte", mensagem);
    Assert.Equal(0, await RetrabalhosDoOriginalAsync(c));
  }

  [Fact]
  public async Task Duas_portas_no_corpo_e_400_PortaInvalida()
  {
    await using var c = await CriarAsync(_factory);
    var perdaId = await DarBaixaAsync(c, c.A, "PerdaArmazem", 1m, incluirTopo: false);
    var numero = $"{await NumeroDoOriginalAsync(c)}-R1";

    var corpos = new object[]
    {
      new { perdaId, reclamacaoId = 1, numero, dataEntrega = SemAtraso, motivoRetrabalho = "Perda" },
      new { perdaId, estruturaItemId = c.A, numero, dataEntrega = SemAtraso, motivoRetrabalho = "Perda" },
      // A quantidade da baixa e a que ela levou; o destino e so da reclamacao.
      new { perdaId, quantidade = 1m, numero, dataEntrega = SemAtraso, motivoRetrabalho = "Perda" },
      new { perdaId, destino = "Refazer", numero, dataEntrega = SemAtraso, motivoRetrabalho = "Perda" },
      new { estruturaItemId = c.A, quantidade = 1m, setorId = c.Dobra, numero, dataEntrega = SemAtraso, motivoRetrabalho = "Perda" },
      // Nenhuma porta: nem baixa, nem reclamacao, nem Peca.
      new { quantidade = 1m, numero, dataEntrega = SemAtraso, motivoRetrabalho = "Perda" },
    };
    foreach (var corpo in corpos)
    {
      var (status, erro, _) = await RecusaAsync(await AbrirAsync(c, c.Qualidade, corpo));
      Assert.Equal((HttpStatusCode.BadRequest, "PortaInvalida"), (status, erro));
    }
    Assert.Equal(0, await RetrabalhosDoOriginalAsync(c));
  }

  [Fact]
  public async Task Numero_repetido_e_409()
  {
    // O 409 do cadastro de Pedido (C7: o numero continua unico, "a guarda de sempre").
    await using var c = await CriarAsync(_factory);

    var resposta = await AbrirAsync(c, c.Pcp, Avulso(c.A, 1m, await NumeroDoOriginalAsync(c)));

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = await CorpoAsync(resposta);
    Assert.Equal(("ValorDuplicado", "numero", c.PedidoId),
        (corpo.GetProperty("erro").GetString(), corpo.GetProperty("campo").GetString(), corpo.GetProperty("idExistente").GetInt32()));
    Assert.Equal(0, await RetrabalhosDoOriginalAsync(c));
  }

  [Fact]
  public async Task Numero_acima_de_trinta_caracteres_e_400_NumeroLongoDemais_no_contrato_de_erro()
  {
    await using var c = await CriarAsync(_factory);

    var resposta = await AbrirAsync(c, c.Pcp, Avulso(c.A, 1m, new string('X', 31)));

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    var corpo = await CorpoAsync(resposta);
    Assert.Equal("NumeroLongoDemais", corpo.GetProperty("erro").GetString());
    Assert.False(string.IsNullOrWhiteSpace(corpo.GetProperty("mensagem").GetString()));
    Assert.Equal(0, await RetrabalhosDoOriginalAsync(c));
  }

  [Fact]
  public async Task Motivo_fora_dos_quatro_e_400_MotivoInvalido()
  {
    await using var c = await CriarAsync(_factory);
    var numero = $"{await NumeroDoOriginalAsync(c)}-R1";

    foreach (var motivo in new[] { null, "Roubo", "perda" })
    {
      var (status, erro, _) = await RecusaAsync(await AbrirAsync(c, c.Pcp, Avulso(c.A, 1m, numero, motivo)));
      Assert.Equal((HttpStatusCode.BadRequest, "MotivoInvalido"), (status, erro));
    }
    Assert.Equal(0, await RetrabalhosDoOriginalAsync(c));
  }

  [Fact]
  public async Task Operador_e_403()
  {
    await using var c = await CriarAsync(_factory);
    var numero = $"{await NumeroDoOriginalAsync(c)}-R1";

    foreach (var quem in new[] { c.Operador, c.Movimentador, c.Gestao, c.Separador })
      Assert.Equal(HttpStatusCode.Forbidden, (await AbrirAsync(c, quem, Avulso(c.A, 1m, numero))).StatusCode);
    Assert.Equal(0, await RetrabalhosDoOriginalAsync(c));
    // A lista e de qualquer autenticado.
    Assert.Equal(HttpStatusCode.OK, (await c.Como(c.Operador).GetAsync($"/api/pedidos/{c.PedidoId}/retrabalhos")).StatusCode);
  }

  [Fact]
  public async Task Listar_traz_o_numero_sugerido_R1_e_depois_R2()
  {
    await using var c = await CriarAsync(_factory);
    var numero = await NumeroDoOriginalAsync(c);
    var operador = c.Como(c.Operador);

    var vazia = await CorpoAsync(await operador.GetAsync($"/api/pedidos/{c.PedidoId}/retrabalhos"));
    Assert.Equal($"{numero}-R1", vazia.GetProperty("numeroSugerido").GetString());
    Assert.Equal(0, vazia.GetProperty("retrabalhos").GetArrayLength());

    var perdaId = await DarBaixaAsync(c, c.A, "PerdaArmazem", 1m, incluirTopo: false);
    await Garantir(await AbrirAsync(c, c.Pcp, Avulso(c.A, 1m, vazia.GetProperty("numeroSugerido").GetString()!)));
    var depois = await CorpoAsync(await operador.GetAsync($"/api/pedidos/{c.PedidoId}/retrabalhos"));
    Assert.Equal($"{numero}-R2", depois.GetProperty("numeroSugerido").GetString());
    await Garantir(await AbrirAsync(c, c.Qualidade, DaBaixa(perdaId, depois.GetProperty("numeroSugerido").GetString()!)));

    var lista = (await CorpoAsync(await operador.GetAsync($"/api/pedidos/{c.PedidoId}/retrabalhos")));
    Assert.Equal($"{numero}-R3", lista.GetProperty("numeroSugerido").GetString());
    var retrabalhos = lista.GetProperty("retrabalhos").EnumerateArray().ToList();
    Assert.Equal(
        new[] { ($"{numero}-R1", "ErroInterno", "Aberto", SemAtraso, "Avulsa", (int?)null), ($"{numero}-R2", "Perda", "Aberto", SemAtraso, "Baixa", (int?)perdaId) },
        retrabalhos.Select(r => (
            r.GetProperty("numero").GetString()!, r.GetProperty("motivoRetrabalho").GetString()!, r.GetProperty("status").GetString()!,
            r.GetProperty("dataEntrega").GetString()!, r.GetProperty("origem").GetProperty("tipo").GetString()!,
            r.GetProperty("origem").GetProperty("id").ValueKind == JsonValueKind.Null ? (int?)null : r.GetProperty("origem").GetProperty("id").GetInt32())));
    Assert.Equal(HttpStatusCode.NotFound, (await operador.GetAsync("/api/pedidos/2000000000/retrabalhos")).StatusCode);
  }

  [Fact]
  public async Task Rota_generica_estorna_a_Devolucao_que_nao_andou()
  {
    await using var c = await CriarAsync(_factory);
    var (peca, _) = await PecaExpedidaAsync(c, 2m);
    var reclamacaoId = await ReclamarAsync(c, peca, 1m);
    var retrabalhoId = (await CorpoAsync(await AbrirAsync(c, c.Qualidade,
        DaReclamacao(reclamacaoId, $"{await NumeroDoOriginalAsync(c)}-R1", "Consertar", c.Dobra)))).GetProperty("id").GetInt32();
    int copia, devolucaoId;
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      copia = await (from e in db.Estruturas.AsNoTracking()
                     join a in db.Agrupamentos.AsNoTracking() on e.AgrupamentoId equals a.Id
                     where a.PedidoId == retrabalhoId
                     select e.Id).SingleAsync();
      devolucaoId = (await db.Movimentacoes.AsNoTracking().SingleAsync(m => m.EstruturaItemId == copia && m.Tipo == "Devolucao")).Id;
    }
    Assert.Equal("EmProducao", await StatusDoPedidoAsync(retrabalhoId));

    var resposta = await c.Como(c.Qualidade).PostAsync($"/api/movimentacoes/{devolucaoId}/estorno", null);

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    // A Peca volta a estar a iniciar (secao 4.7 da spec da Fase 5B-2). O status nao regride, como no estorno de um
    // `Inicio`: o recalculo so devolve a `Aberto` o Pedido que estava `Aberto` ou `Concluido` (C23 da spec da Fase 5B-1).
    using var escopo2 = _factory.Services.CreateScope();
    var db2 = escopo2.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var linhas = await db2.Movimentacoes.AsNoTracking().Where(m => m.EstruturaItemId == copia).ToListAsync();
    Assert.Equal(("AguardandoConserto", "AIniciar"),
        (linhas.Single(m => m.Tipo == "Estorno").OrigemPosicao, linhas.Single(m => m.Tipo == "Estorno").DestinoPosicao));
    Assert.Equal("EmProducao", await StatusDoPedidoAsync(retrabalhoId));
    Assert.Equal(HttpStatusCode.Conflict,
        (await c.Como(c.Qualidade).PostAsync($"/api/movimentacoes/{devolucaoId}/estorno", null)).StatusCode);
  }

  private async Task<string> StatusDoPedidoAsync(int pedidoId)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    return (await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == pedidoId)).Status;
  }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using static Rastreamento.Api.Tests.CenarioDaFase3NaApi;

namespace Rastreamento.Api.Tests;

/// <summary>
/// `GET /panorama` (secao 5.1 da spec do Panorama) no cenario da Fase 3. A Peca A (10, Solda -> Pintura) tem os Itens
/// B (Corte) e C (Dobra) e, dentro de B, o neto "Neto" (Pintura): so o neto e o C sao iniciados, em dois Setores e em
/// dois niveis. Uma Peca D (6, Corte -> Dobra) e dividida em dois passos, uma Peca E (2) e expedida inteira e o
/// Pedido tem um Retrabalho avulso, aberto antes de o Pedido ser pausado. A lista e global (todo Pedido nao encerrado
/// do banco compartilhado), entao toda asserção e escopada nos dois Pedidos do cenario.
/// </summary>
public class PanoramaEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private const string SemAtraso = "2099-12-31";

  private readonly WebApplicationFactory<Program> _factory;

  public PanoramaEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  private static async Task<int> PecaNovaAsync(CenarioDaFase3NaApi c, decimal quantidade, params int[] passos)
  {
    var pcp = c.Como(c.Pcp);
    var criada = await pcp.PostAsJsonAsync($"/api/agrupamentos/{c.AgrupamentoId}/estrutura",
        new { componenteId = c.ComponenteId, quantidade, requerRelatorioDimensional = false });
    await Garantir(criada);
    var peca = (await CorpoAsync(criada)).GetProperty("id").GetInt32();
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{peca}/roteiro", new { passos }));
    return peca;
  }

  private static async Task IniciarAsync(CenarioDaFase3NaApi c, int no, int setor, decimal quantidade) =>
      await Garantir(await c.Como(c.Operador).PostAsJsonAsync(
          $"/api/estrutura/{no}/inicios", new { setorId = setor, quantidade }));

  private static async Task TerminarAsync(CenarioDaFase3NaApi c, int no, int setor, int ordem, decimal quantidade) =>
      await Garantir(await c.Como(c.Operador).PostAsJsonAsync(
          $"/api/estrutura/{no}/terminos", new { setorId = setor, ordem, quantidade }));

  private static async Task EntregarAsync(CenarioDaFase3NaApi c, int no, int setor, int ordem, decimal quantidade) =>
      await Garantir(await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new
      {
        itens = new[]
        {
          new
          {
            estruturaItemId = no, origem = new { posicao = "AguardandoColeta", setorId = setor, ordem = (int?)ordem },
            destinoSetorId = (int?)null, quantidade,
          },
        },
      }));

  [Fact]
  public async Task Get_panorama_devolve_a_forma_com_peca_dividida_pausada_retrabalho_e_itens_em()
  {
    await using var c = await CriarAsync(_factory);
    var pcp = c.Como(c.Pcp);

    // Dois niveis e dois Setores: o neto de B na Pintura e o C na Dobra. A e B continuam a iniciar.
    var neto = (await CorpoAsync(await pcp.PostAsJsonAsync($"/api/estrutura/{c.B}/filhos",
        new { componenteId = (int?)null, descricao = "Neto", quantidade = 40m, quantidadePorPai = 2m }))).GetProperty("id").GetInt32();
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{neto}/roteiro", new { passos = new[] { c.Pintura } }));
    await IniciarAsync(c, neto, c.Pintura, 4m);
    await IniciarAsync(c, c.C, c.Dobra, 2m);

    // D dividida: 6 iniciadas no Corte, 3 terminadas e 2 entregues ao passo da Dobra.
    var d = await PecaNovaAsync(c, 6m, c.Corte, c.Dobra);
    await IniciarAsync(c, d, c.Corte, 6m);
    await TerminarAsync(c, d, c.Corte, 1, 3m);
    await EntregarAsync(c, d, c.Corte, 1, 2m);

    // E expedida inteira: nao aparece.
    var e = await PecaNovaAsync(c, 2m, c.Corte);
    await IniciarAsync(c, e, c.Corte, 2m);
    await TerminarAsync(c, e, c.Corte, 1, 2m);
    await EntregarAsync(c, e, c.Corte, 1, 2m);
    await Garantir(await c.Como(c.Qualidade).PostAsJsonAsync("/api/remessas",
        new { itens = new[] { new { estruturaItemId = e, quantidade = 2m } } }));

    // O Retrabalho avulso de A, e so depois a pausa do Pedido original.
    var numero = (await CorpoAsync(await pcp.GetAsync($"/api/pedidos/{c.PedidoId}"))).GetProperty("numero").GetString();
    var retrabalho = await c.Como(c.Pcp).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/retrabalhos", new
    {
      estruturaItemId = c.A, quantidade = 3m, numero = $"{numero}-R1", dataEntrega = SemAtraso, motivoRetrabalho = "ErroInterno",
    });
    await Garantir(retrabalho);
    var retrabalhoId = (await CorpoAsync(retrabalho)).GetProperty("id").GetInt32();
    await Garantir(await c.Como(c.Gestao).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/pausas", new { motivo = "Alteração de projeto" }));

    var resposta = await c.Como(c.Qualidade).GetAsync("/api/panorama");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var pecas = (await CorpoAsync(resposta)).GetProperty("pecas").EnumerateArray()
        .Where(p => p.GetProperty("pedido").GetProperty("id").GetInt32() is var id && (id == c.PedidoId || id == retrabalhoId))
        .ToList();
    // Peca A e D do original (B, C, o neto e a E expedida nao entram) e a copia de A no Retrabalho, em C9: o prazo
    // (os dois de 2099-12-31), o numero do Pedido (o original vem antes do "-R1"), o codigo e o Id.
    Assert.Equal(new[] { c.A, d }, pecas.Where(p => p.GetProperty("pedido").GetProperty("id").GetInt32() == c.PedidoId)
        .Select(p => p.GetProperty("estruturaItemId").GetInt32()));
    var copia = Assert.Single(pecas, p => p.GetProperty("pedido").GetProperty("id").GetInt32() == retrabalhoId);

    var a = pecas.Single(p => p.GetProperty("estruturaItemId").GetInt32() == c.A);
    Assert.Equal(c.AgrupamentoId, a.GetProperty("agrupamentoId").GetInt32());
    Assert.Equal(10m, a.GetProperty("quantidade").GetDecimal());
    var pedidoDeA = a.GetProperty("pedido");
    Assert.Equal((numero, SemAtraso, false, false), (
        pedidoDeA.GetProperty("numero").GetString(), pedidoDeA.GetProperty("dataEntrega").GetString(),
        pedidoDeA.GetProperty("atrasado").GetBoolean(), pedidoDeA.GetProperty("retrabalho").GetBoolean()));
    Assert.Equal("Alteração de projeto", pedidoDeA.GetProperty("pausa").GetProperty("motivo").GetString());
    var componente = a.GetProperty("componente");
    Assert.Equal((c.ComponenteId, "Chassi da Fase 3", JsonValueKind.Null), (
        componente.GetProperty("id").GetInt32(), componente.GetProperty("descricao").GetString(),
        componente.GetProperty("miniaturaVersao").ValueKind));
    Assert.Equal(new[] { c.Dobra, c.Pintura }, a.GetProperty("itensEm").EnumerateArray().Select(s => s.GetProperty("setorId").GetInt32()));
    Assert.All(a.GetProperty("itensEm").EnumerateArray(), s => Assert.Contains("f3-", s.GetProperty("nome").GetString()));
    var segmentosDeA = a.GetProperty("distribuicao").EnumerateArray().ToList();
    Assert.Equal(("AIniciar", 10m), (segmentosDeA.Single().GetProperty("tipo").GetString(), segmentosDeA.Single().GetProperty("quantidade").GetDecimal()));
    Assert.Equal(JsonValueKind.Null, segmentosDeA.Single().GetProperty("setorId").ValueKind);
    Assert.Equal(("SemRelatorio", false), (
        a.GetProperty("afericao").GetProperty("situacao").GetString(), a.GetProperty("afericao").GetProperty("exigeRelatorio").GetBoolean()));
    var embalagem = a.GetProperty("embalagem");
    Assert.Equal((false, false, 10m, JsonValueKind.Null), (
        embalagem.GetProperty("vale").GetBoolean(), embalagem.GetProperty("podeMarcar").GetBoolean(),
        embalagem.GetProperty("emProducao").GetDecimal(), embalagem.GetProperty("ultima").ValueKind));

    // A copia no Retrabalho: marcada, sem pausa (a pausa e do Pedido original), ninguem a comecou.
    Assert.True(copia.GetProperty("pedido").GetProperty("retrabalho").GetBoolean());
    Assert.Equal(JsonValueKind.Null, copia.GetProperty("pedido").GetProperty("pausa").ValueKind);
    Assert.Equal(3m, copia.GetProperty("quantidade").GetDecimal());

    // D: um segmento por passo, na ordem do Roteiro, com o aguardando coleta dentro do passo.
    var segmentosDeD = pecas.Single(p => p.GetProperty("estruturaItemId").GetInt32() == d)
        .GetProperty("distribuicao").EnumerateArray()
        .Select(s => (
            s.GetProperty("tipo").GetString()!, s.GetProperty("setorId").GetInt32(), s.GetProperty("ordem").GetInt32(),
            s.GetProperty("quantidade").GetDecimal(), s.GetProperty("aguardandoColeta").GetDecimal()))
        .ToArray();
    Assert.Equal(new[] { ("Passo", c.Corte, 1, 4m, 1m), ("Passo", c.Dobra, 2, 2m, 0m) }, segmentosDeD);
  }

  // ------------------------------------------------------------ o "Embalado" (secoes 3.2, 3.3 e 5.2)

  /// <summary>Uma Peca nova de `quantidade`, com `noLocal` unidades no local de expedicao e o resto a iniciar.</summary>
  private static async Task<int> PecaNoLocalAsync(CenarioDaFase3NaApi c, decimal quantidade, decimal noLocal)
  {
    var peca = await PecaNovaAsync(c, quantidade, c.Corte);
    await IniciarAsync(c, peca, c.Corte, noLocal);
    await TerminarAsync(c, peca, c.Corte, 1, noLocal);
    await EntregarAsync(c, peca, c.Corte, 1, noLocal);
    return peca;
  }

  private static Task<HttpResponseMessage> Embalar(CenarioDaFase3NaApi c, UsuarioDeTeste quem, int no, object corpo) =>
      c.Como(quem).PostAsJsonAsync($"/api/estrutura/{no}/embalagem", corpo);

  private static async Task<JsonElement> EmbalagemNoGetAsync(CenarioDaFase3NaApi c, int peca) =>
      (await CorpoAsync(await c.Como(c.Qualidade).GetAsync("/api/panorama"))).GetProperty("pecas").EnumerateArray()
          .Single(p => p.GetProperty("estruturaItemId").GetInt32() == peca).GetProperty("embalagem");

  [Fact]
  public async Task Qualidade_marca_e_o_get_mostra_vale()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNoLocalAsync(c, 4m, 4m);

    var marcou = await Embalar(c, c.Qualidade, peca, new { embalado = true });

    Assert.Equal(HttpStatusCode.NoContent, marcou.StatusCode);
    var vale = await EmbalagemNoGetAsync(c, peca);
    Assert.Equal((true, true, 0m, "Marcar"), (
        vale.GetProperty("vale").GetBoolean(), vale.GetProperty("podeMarcar").GetBoolean(),
        vale.GetProperty("emProducao").GetDecimal(), vale.GetProperty("ultima").GetProperty("tipo").GetString()));
    Assert.Equal("Usuario de Teste", vale.GetProperty("ultima").GetProperty("usuario").GetString());

    var jaEmbalado = await Embalar(c, c.Qualidade, peca, new { embalado = true });
    Assert.Equal(HttpStatusCode.Conflict, jaEmbalado.StatusCode);
    var corpo = await CorpoAsync(jaEmbalado);
    Assert.Equal(("JaEmbalado", "Esta Peça já está embalada."), (
        corpo.GetProperty("erro").GetString(), corpo.GetProperty("mensagem").GetString()));

    var desmarcou = await Embalar(c, c.Pcp, peca, new { embalado = false });
    Assert.Equal(HttpStatusCode.NoContent, desmarcou.StatusCode);
    Assert.False((await EmbalagemNoGetAsync(c, peca)).GetProperty("vale").GetBoolean());

    var naoEmbalado = await Embalar(c, c.Qualidade, peca, new { embalado = false });
    Assert.Equal(HttpStatusCode.Conflict, naoEmbalado.StatusCode);
    Assert.Equal("NaoEmbalado", (await CorpoAsync(naoEmbalado)).GetProperty("erro").GetString());
  }

  [Fact]
  public async Task Gestao_recebe_403_sem_corpo_no_post()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNoLocalAsync(c, 2m, 2m);

    var resposta = await Embalar(c, c.Gestao, peca, new { embalado = true });

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    Assert.Equal(string.Empty, await resposta.Content.ReadAsStringAsync());
    Assert.False((await EmbalagemNoGetAsync(c, peca)).GetProperty("vale").GetBoolean());
  }

  [Fact]
  public async Task Operador_recebe_403_no_post()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNoLocalAsync(c, 2m, 2m);

    var resposta = await Embalar(c, c.Operador, peca, new { embalado = true });

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
  }

  [Fact]
  public async Task Marcar_com_producao_em_andamento_e_409_ProducaoNaoTerminou()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNoLocalAsync(c, 5m, 3m);

    var resposta = await Embalar(c, c.Qualidade, peca, new { embalado = true });

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = await CorpoAsync(resposta);
    Assert.Equal(("ProducaoNaoTerminou", "Faltam 2 em produção."), (
        corpo.GetProperty("erro").GetString(), corpo.GetProperty("mensagem").GetString()));
    Assert.False((await EmbalagemNoGetAsync(c, peca)).GetProperty("vale").GetBoolean());
  }

  [Fact]
  public async Task Recusas_de_corpo_de_no_e_de_item()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNoLocalAsync(c, 2m, 2m);

    var semCampo = await Embalar(c, c.Qualidade, peca, new { });
    Assert.Equal(HttpStatusCode.BadRequest, semCampo.StatusCode);
    Assert.Equal("CorpoInvalido", (await CorpoAsync(semCampo)).GetProperty("erro").GetString());

    var inexistente = await Embalar(c, c.Qualidade, 999999999, new { embalado = true });
    Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);

    var item = await Embalar(c, c.Qualidade, c.B, new { embalado = true });
    Assert.Equal(HttpStatusCode.BadRequest, item.StatusCode);
    var corpo = await CorpoAsync(item);
    Assert.Equal("NaoEhPeca", corpo.GetProperty("erro").GetString());
    Assert.EndsWith("é um Item: só a Peça é embalada.", corpo.GetProperty("mensagem").GetString());
  }

  [Fact]
  public async Task Estorno_de_baixa_feita_num_setor_invalida_a_marca()
  {
    await using var c = await CriarAsync(_factory);
    // 3 unidades: 1 expedida, 1 no local de expedicao e 1 que sera baixada no Setor.
    var peca = await PecaNovaAsync(c, 3m, c.Corte);
    await IniciarAsync(c, peca, c.Corte, 3m);
    await TerminarAsync(c, peca, c.Corte, 1, 2m);
    await EntregarAsync(c, peca, c.Corte, 1, 2m);
    var qualidade = c.Como(c.Qualidade);
    await Garantir(await qualidade.PostAsJsonAsync("/api/remessas",
        new { itens = new[] { new { estruturaItemId = peca, quantidade = 1m } } }));
    var baixa = await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/baixas", new
    {
      motivo = "MortaEmProcesso", observacao = (string?)null, tudo = false, incluirTopo = false,
      origens = new[] { new { posicao = "NoSetor", setorId = (int?)c.Corte, ordem = (int?)1, quantidade = 1m } },
    });
    await Garantir(baixa);
    await Garantir(await Embalar(c, c.Qualidade, peca, new { embalado = true }));
    Assert.True((await EmbalagemNoGetAsync(c, peca)).GetProperty("vale").GetBoolean());

    var baixaId = (await CorpoAsync(baixa)).GetProperty("id").GetInt32();
    await Garantir(await qualidade.PostAsync($"/api/baixas/{baixaId}/estorno", null));

    var depois = await EmbalagemNoGetAsync(c, peca);
    Assert.Equal((false, false, 1m), (
        depois.GetProperty("vale").GetBoolean(), depois.GetProperty("podeMarcar").GetBoolean(),
        depois.GetProperty("emProducao").GetDecimal()));
  }

  [Fact]
  public async Task Estorno_de_remessa_nao_invalida_a_marca()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNoLocalAsync(c, 2m, 2m);
    var qualidade = c.Como(c.Qualidade);
    var remessa = await qualidade.PostAsJsonAsync("/api/remessas",
        new { itens = new[] { new { estruturaItemId = peca, quantidade = 1m } } });
    await Garantir(remessa);
    await Garantir(await Embalar(c, c.Qualidade, peca, new { embalado = true }));
    Assert.True((await EmbalagemNoGetAsync(c, peca)).GetProperty("vale").GetBoolean());

    var remessaId = (await CorpoAsync(remessa)).GetProperty("remessa").GetProperty("id").GetInt32();
    await Garantir(await qualidade.PostAsync($"/api/remessas/{remessaId}/estorno", null));

    Assert.True((await EmbalagemNoGetAsync(c, peca)).GetProperty("vale").GetBoolean());
  }

  [Theory]
  [InlineData("Operador")]
  [InlineData("Movimentador")]
  [InlineData("Separador")]
  [InlineData("Qualidade")]
  [InlineData("Gestao")]
  [InlineData("Pcp")]
  public async Task Get_panorama_e_de_qualquer_perfil(string perfil)
  {
    await using var c = await CriarAsync(_factory);
    var quem = perfil switch
    {
      "Operador" => c.Operador, "Movimentador" => c.Movimentador, "Separador" => c.Separador,
      "Qualidade" => c.Qualidade, "Gestao" => c.Gestao, _ => c.Pcp,
    };

    var resposta = await c.Como(quem).GetAsync("/api/panorama");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
  }

  [Fact]
  public async Task Get_panorama_sem_login_e_401()
  {
    var resposta = await _factory.CreateClient().GetAsync("/api/panorama");

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }

  // ------------------------------------------------------------ a idade e as complicacoes (Fase 6A, secao 5.2)

  private static async Task<JsonElement> PecaNoGetAsync(CenarioDaFase3NaApi c, int peca) =>
      (await CorpoAsync(await c.Como(c.Qualidade).GetAsync("/api/panorama"))).GetProperty("pecas").EnumerateArray()
          .Single(p => p.GetProperty("estruturaItemId").GetInt32() == peca);

  private static DateTimeOffset Data(JsonElement e) => DateTimeOffset.Parse(e.GetString()!);

  private static JsonElement Segmento(JsonElement peca, string tipo) =>
      peca.GetProperty("distribuicao").EnumerateArray().Single(s => s.GetProperty("tipo").GetString() == tipo);

  private static async Task<int> EsperarAsync(int ms)
  {
    await Task.Delay(ms);
    return ms;
  }

  [Fact]
  public async Task Panorama_traz_agora_e_desde_de_cada_segmento()
  {
    await using var c = await CriarAsync(_factory);
    // A: filhos e so a iniciar. Solta: sem filhos, a iniciar. D: dois passos, com parte esperando coleta, e a F no local.
    var solta = await PecaNovaAsync(c, 3m, c.Corte);
    var d = await PecaNovaAsync(c, 6m, c.Corte, c.Dobra);
    await IniciarAsync(c, d, c.Corte, 6m);
    await TerminarAsync(c, d, c.Corte, 1, 3m);
    await EntregarAsync(c, d, c.Corte, 1, 2m);
    var f = await PecaNoLocalAsync(c, 4m, 4m);

    var corpo = await CorpoAsync(await c.Como(c.Qualidade).GetAsync("/api/panorama"));
    var agora = Data(corpo.GetProperty("agora"));
    var todas = corpo.GetProperty("pecas").EnumerateArray().ToList();
    JsonElement Da(int id) => todas.Single(p => p.GetProperty("estruturaItemId").GetInt32() == id);

    Assert.True(Math.Abs((DateTimeOffset.UtcNow - agora).TotalMinutes) < 5, "agora e o relogio do servidor");

    // C5: o "a iniciar" de A, que tem filhos, nao tem data; o da Peca solta, sim (a criacao do Agrupamento).
    Assert.Equal(JsonValueKind.Null, Segmento(Da(c.A), "AIniciar").GetProperty("desde").ValueKind);
    var criacao = Data(Segmento(Da(solta), "AIniciar").GetProperty("desde"));
    Assert.True(criacao <= agora);

    // D: o passo da Corte tem as duas datas (a parte no Setor e a que espera coleta), a da Dobra so a do Setor.
    var corte = Da(d).GetProperty("distribuicao").EnumerateArray().Single(s => s.GetProperty("ordem").ValueKind == JsonValueKind.Number && s.GetProperty("ordem").GetInt32() == 1);
    var dobra = Da(d).GetProperty("distribuicao").EnumerateArray().Single(s => s.GetProperty("ordem").ValueKind == JsonValueKind.Number && s.GetProperty("ordem").GetInt32() == 2);
    var noCorte = Data(corte.GetProperty("desde"));
    var coletaDaCorte = Data(corte.GetProperty("aguardandoColetaDesde"));
    var noDobra = Data(dobra.GetProperty("desde"));
    Assert.True(criacao <= noCorte && noCorte <= coletaDaCorte && coletaDaCorte <= agora);
    Assert.True(noDobra >= coletaDaCorte && noDobra <= agora);
    Assert.Equal(JsonValueKind.Null, dobra.GetProperty("aguardandoColetaDesde").ValueKind);

    // O local de expedicao tem a data; Expedido e Perdido nunca (nao existem nestas Pecas).
    Assert.True(Data(Segmento(Da(f), "Expedicao").GetProperty("desde")) <= agora);
  }

  [Fact]
  public async Task Parado_desde_e_a_parte_mais_antiga_que_conta()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNovaAsync(c, 6m, c.Corte);
    await IniciarAsync(c, peca, c.Corte, 3m);
    await EsperarAsync(30);
    await IniciarAsync(c, peca, c.Corte, 3m);
    await EsperarAsync(30);
    await TerminarAsync(c, peca, c.Corte, 1, 3m);

    var no = await PecaNoGetAsync(c, peca);

    // O Termino tira a parte mais antiga (a do primeiro Inicio): sobram a segunda no Setor e a que espera coleta.
    var passo = Segmento(no, "Passo");
    var noSetor = Data(passo.GetProperty("desde"));
    var coleta = Data(passo.GetProperty("aguardandoColetaDesde"));
    Assert.True(noSetor < coleta);
    Assert.Equal(noSetor, Data(no.GetProperty("paradoDesde")));
  }

  [Fact]
  public async Task Peca_com_filhos_so_a_iniciar_tem_parado_desde_dos_filhos()
  {
    // Review Focus (C5): o "a iniciar" de A nao conta porque A tem filhos; quem esta parado sao B e C.
    await using var c = await CriarAsync(_factory);
    var solta = await PecaNovaAsync(c, 3m, c.Corte);
    var criacao = Data(Segmento(await PecaNoGetAsync(c, solta), "AIniciar").GetProperty("desde"));
    await EsperarAsync(30);
    await IniciarAsync(c, c.B, c.Corte, 20m);
    await IniciarAsync(c, c.C, c.Dobra, 10m);

    var a = await PecaNoGetAsync(c, c.A);

    // Sem a regra, o "a iniciar" de A (que e a criacao do Agrupamento) seria o mais antigo.
    Assert.Equal(JsonValueKind.Null, Segmento(a, "AIniciar").GetProperty("desde").ValueKind);
    Assert.True(Data(a.GetProperty("paradoDesde")) > criacao);
  }

  [Fact]
  public async Task Estorno_devolve_a_parte_com_a_data_original()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNovaAsync(c, 4m, c.Corte);
    await IniciarAsync(c, peca, c.Corte, 4m);
    var antes = Data(Segmento(await PecaNoGetAsync(c, peca), "Passo").GetProperty("desde"));
    await EsperarAsync(30);
    var termino = await CorpoAsync(await c.Como(c.Operador).PostAsJsonAsync(
        $"/api/estrutura/{peca}/terminos", new { setorId = c.Corte, ordem = 1, quantidade = 4m }));
    var depoisDoTermino = await PecaNoGetAsync(c, peca);
    Assert.True(Data(Segmento(depoisDoTermino, "Passo").GetProperty("aguardandoColetaDesde")) > antes);

    await Garantir(await c.Como(c.Operador).PostAsync($"/api/movimentacoes/{termino.GetProperty("id").GetInt32()}/estorno", null));

    var passo = Segmento(await PecaNoGetAsync(c, peca), "Passo");
    Assert.Equal(antes, Data(passo.GetProperty("desde")));
    Assert.Equal(JsonValueKind.Null, passo.GetProperty("aguardandoColetaDesde").ValueKind);
  }

  [Fact]
  public async Task Complicacao_de_material_pendente()
  {
    await using var c = await CriarAsync(_factory);
    var parafuso = await c.CriarMaterialAsync("PAR");
    await Garantir(await c.Como(c.Pcp).PutAsJsonAsync($"/api/estrutura/{c.A}/materiais",
        new { linhas = new[] { new { materialId = parafuso, quantidade = 10m } } }));

    var a = await PecaNoGetAsync(c, c.A);

    var complicacao = Assert.Single(a.GetProperty("complicacoes").EnumerateArray());
    Assert.Equal(("MaterialPendente", "Material não separado: Material PAR"), (
        complicacao.GetProperty("codigo").GetString(), complicacao.GetProperty("texto").GetString()));

    await Garantir(await c.Como(c.Separador).PostAsJsonAsync($"/api/estrutura/{c.A}/separacoes",
        new { linhas = new[] { new { materialId = parafuso, quantidade = 10m } } }));
    Assert.Empty((await PecaNoGetAsync(c, c.A)).GetProperty("complicacoes").EnumerateArray());
  }

  [Fact]
  public async Task Complicacao_de_aguardando_liberacao()
  {
    await using var c = await CriarAsync(_factory);
    var pcp = c.Como(c.Pcp);
    var criada = await pcp.PostAsJsonAsync($"/api/agrupamentos/{c.AgrupamentoId}/estrutura",
        new { componenteId = c.ComponenteId, quantidade = 6m, requerRelatorioDimensional = true });
    await Garantir(criada);
    var peca = (await CorpoAsync(criada)).GetProperty("id").GetInt32();
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{peca}/roteiro", new { passos = new[] { c.Corte } }));
    await IniciarAsync(c, peca, c.Corte, 6m);
    await TerminarAsync(c, peca, c.Corte, 1, 6m);
    await EntregarAsync(c, peca, c.Corte, 1, 6m);
    var qualidade = c.Como(c.Qualidade);
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/afericoes", new { resultado = "NaoConforme" }));
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/contagens", new { conformes = 4m, naoConformes = 2m }));

    var antes = (await PecaNoGetAsync(c, peca)).GetProperty("complicacoes").EnumerateArray()
        .Select(x => (x.GetProperty("codigo").GetString()!, x.GetProperty("texto").GetString()!)).ToArray();
    Assert.Equal(new[] { ("AguardandoDecisao", "Não conformes aguardando decisão (2)") }, antes);

    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/pedidos-de-liberacao",
        new { quantidade = 2m, justificativa = "Aba fora de medida; o cliente aceita." }));

    var depois = (await PecaNoGetAsync(c, peca)).GetProperty("complicacoes").EnumerateArray()
        .Select(x => (x.GetProperty("codigo").GetString()!, x.GetProperty("texto").GetString()!)).ToArray();
    Assert.Equal(new[] { ("AguardandoLiberacao", "Aguardando liberação da Gestão (2)") }, depois);
  }

  [Fact]
  public async Task Complicacao_a_repor_so_na_peca_da_baixa()
  {
    await using var c = await CriarAsync(_factory);
    var perdida = await PecaNovaAsync(c, 6m, c.Corte);
    var intacta = await PecaNovaAsync(c, 6m, c.Corte);
    await IniciarAsync(c, perdida, c.Corte, 6m);
    await IniciarAsync(c, intacta, c.Corte, 6m);
    await Garantir(await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{perdida}/baixas", new
    {
      motivo = "MortaEmProcesso", observacao = (string?)null, tudo = false,
      origens = new[] { new { posicao = "NoSetor", setorId = (int?)c.Corte, ordem = (int?)1, quantidade = 2m } },
      incluirTopo = false,
    }));

    var daBaixa = (await PecaNoGetAsync(c, perdida)).GetProperty("complicacoes").EnumerateArray()
        .Select(x => (x.GetProperty("codigo").GetString()!, x.GetProperty("texto").GetString()!)).ToArray();
    Assert.Equal(new[] { ("ARepor", "2 a repor") }, daBaixa);
    // Nem a Peca irma, do mesmo Pedido, nem a A: o Pedido ter algo a repor nao marca as outras.
    Assert.Empty((await PecaNoGetAsync(c, intacta)).GetProperty("complicacoes").EnumerateArray());
    Assert.DoesNotContain((await PecaNoGetAsync(c, c.A)).GetProperty("complicacoes").EnumerateArray(),
        x => x.GetProperty("codigo").GetString() == "ARepor");
  }
}

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
/// A baixa de um no (secoes 4.1 a 4.6 e 5.1 a 5.4 da spec da Fase 5B-1) no cenario da Fase 3: Peca A (10, copiada do
/// catalogo, sem descricao propria, "Chassi da Fase 3"; Solda -> Pintura), B (20, razao 2, "Suporte"; Corte) e C (10,
/// razao 1, "Calço"; Dobra). Toda frase que nomeia um no usa A ou um no copiado do catalogo. Toda assercao escopada
/// nos nos e no Pedido do cenario.
/// </summary>
public class BaixaEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;

  public BaixaEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  private static object Origem(string posicao, int? setorId, int? ordem, decimal quantidade) =>
      new { posicao, setorId, ordem, quantidade };

  private static object Parte(string motivo, params object[] origens) =>
      new { motivo, observacao = (string?)null, tudo = false, origens, incluirTopo = false };

  private static async Task IniciarAsync(CenarioDaFase3NaApi c, int no, int setor, decimal quantidade) =>
      await Garantir(await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{no}/inicios", new { setorId = setor, quantidade }));

  /// <summary>Iniciar, terminar e entregar para a montagem de A na Solda.</summary>
  private static async Task LevarParaAMontagemAsync(CenarioDaFase3NaApi c, int no, int setor, decimal quantidade)
  {
    await IniciarAsync(c, no, setor, quantidade);
    await Garantir(await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{no}/terminos", new { setorId = setor, ordem = 1, quantidade }));
    await Garantir(await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new
    {
      itens = new[]
      {
        new { estruturaItemId = no, origem = new { posicao = "AguardandoColeta", setorId = setor, ordem = (int?)1 }, destinoSetorId = (int?)null, quantidade },
      },
    }));
  }

  /// <summary>Os saldos do no, por `GET /agrupamentos/{id}/posicoes`: (posicao, Setor, passo) -> quantidade.</summary>
  private static async Task<List<(string Posicao, int? SetorId, int? Ordem, decimal Quantidade)>> SaldosAsync(
      CenarioDaFase3NaApi c, int no) =>
      (await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/agrupamentos/{c.AgrupamentoId}/posicoes")))
          .EnumerateArray()
          .Single(p => p.GetProperty("estruturaItemId").GetInt32() == no)
          .GetProperty("saldos").EnumerateArray()
          .Select(s => (
              s.GetProperty("posicao").GetString()!,
              s.GetProperty("setorId").ValueKind == JsonValueKind.Null ? (int?)null : s.GetProperty("setorId").GetInt32(),
              s.GetProperty("ordem").ValueKind == JsonValueKind.Null ? (int?)null : s.GetProperty("ordem").GetInt32(),
              s.GetProperty("quantidade").GetDecimal()))
          .ToList();

  private static async Task<decimal> SaldoAsync(CenarioDaFase3NaApi c, int no, string posicao) =>
      (await SaldosAsync(c, no)).Where(s => s.Posicao == posicao).Sum(s => s.Quantidade);

  /// <summary>As linhas da baixa como (no, posicao de origem, quantidade), na ordem da resposta.</summary>
  private static List<(int No, string Origem, decimal Quantidade)> Linhas(JsonElement corpo) =>
      corpo.GetProperty("linhas").EnumerateArray()
          .Select(l => (l.GetProperty("estruturaItemId").GetInt32(), l.GetProperty("origemPosicao").GetString()!,
              l.GetProperty("quantidade").GetDecimal()))
          .ToList();

  /// <summary>
  /// Todos os campos de cada linha. A quantidade pelo valor: a gravada volta do banco com quatro casas, e o texto do
  /// JSON (`3` contra `3.0000`) mudaria sem mudar o numero.
  /// </summary>
  private static List<string> LinhasInteiras(JsonElement corpo) =>
      corpo.GetProperty("linhas").EnumerateArray()
          .Select(l => string.Join("|",
              l.GetProperty("estruturaItemId").GetInt32(), l.GetProperty("descricao").GetString(),
              l.GetProperty("codigo").ToString(), l.GetProperty("origemPosicao").GetString(),
              l.GetProperty("origemSetorId").ToString(), l.GetProperty("origemSetorNome").ToString(),
              l.GetProperty("origemOrdem").ToString(), l.GetProperty("quantidade").GetDecimal().ToString("0.####")))
          .ToList();

  private async Task<int> PerdasDoPedidoAsync(CenarioDaFase3NaApi c)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    return await db.Perdas.AsNoTracking().CountAsync(p => p.PedidoId == c.PedidoId);
  }

  private static async Task<(HttpStatusCode Status, string? Erro, string? Mensagem)> RecusaAsync(HttpResponseMessage resposta)
  {
    var texto = await resposta.Content.ReadAsStringAsync();
    if (string.IsNullOrEmpty(texto)) return (resposta.StatusCode, null, null);
    var corpo = JsonDocument.Parse(texto).RootElement;
    return (resposta.StatusCode, corpo.GetProperty("erro").GetString(),
        corpo.TryGetProperty("mensagem", out var m) ? m.GetString() : null);
  }

  /// <summary>Um filho de A copiado do catalogo, sem descricao propria (o nome e "Chassi da Fase 3").</summary>
  private static async Task<int> FilhoDoCatalogoAsync(CenarioDaFase3NaApi c)
  {
    var criado = await c.Como(c.Pcp).PostAsJsonAsync($"/api/estrutura/{c.A}/filhos",
        new { componenteId = (int?)c.ComponenteId, descricao = (string?)null, quantidade = 10m, quantidadePorPai = 1m });
    await Garantir(criado);
    return (await CorpoAsync(criado)).GetProperty("id").GetInt32();
  }

  [Fact]
  public async Task Baixa_de_parte_grava_uma_Perda_e_as_linhas_e_devolve_201()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 5m);

    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.B}/baixas", new
    {
      motivo = "MortaEmProcesso", observacao = "  Quebrou na serra  ", tudo = false,
      origens = new[] { Origem("NoSetor", c.Corte, 1, 2m) }, incluirTopo = false,
    });

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var baixa = await CorpoAsync(resposta);
    Assert.True(baixa.GetProperty("id").GetInt32() > 0);
    Assert.Equal("MortaEmProcesso", baixa.GetProperty("motivo").GetString());
    Assert.Equal("Quebrou na serra", baixa.GetProperty("observacao").GetString());
    Assert.Equal("No", baixa.GetProperty("escopo").GetProperty("tipo").GetString());
    Assert.Equal(c.B, baixa.GetProperty("escopo").GetProperty("id").GetInt32());
    Assert.False(baixa.GetProperty("tudo").GetBoolean());
    Assert.EndsWith("-03:00", baixa.GetProperty("dataHora").GetString());
    Assert.Equal(c.Qualidade.Id, baixa.GetProperty("usuarioId").GetInt32());
    Assert.Equal("Usuario de Teste", baixa.GetProperty("usuarioNome").GetString());
    Assert.False(baixa.GetProperty("estornada").GetBoolean());
    Assert.True(baixa.GetProperty("estornavel").GetBoolean());
    var linha = Assert.Single(baixa.GetProperty("linhas").EnumerateArray());
    Assert.Equal(c.B, linha.GetProperty("estruturaItemId").GetInt32());
    Assert.Equal("Suporte", linha.GetProperty("descricao").GetString());
    Assert.Equal(JsonValueKind.Null, linha.GetProperty("codigo").ValueKind);
    Assert.Equal("NoSetor", linha.GetProperty("origemPosicao").GetString());
    Assert.Equal(c.Corte, linha.GetProperty("origemSetorId").GetInt32());
    Assert.StartsWith("f3-", linha.GetProperty("origemSetorNome").GetString());
    Assert.EndsWith("-Corte", linha.GetProperty("origemSetorNome").GetString());
    Assert.Equal(1, linha.GetProperty("origemOrdem").GetInt32());
    Assert.Equal(2m, linha.GetProperty("quantidade").GetDecimal());

    Assert.Equal(2m, await SaldoAsync(c, c.B, "Perdido"));
    Assert.Equal(3m, await SaldoAsync(c, c.B, "NoSetor"));
    Assert.Equal(1, await PerdasDoPedidoAsync(c));

    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var perda = await db.Perdas.AsNoTracking().SingleAsync(p => p.PedidoId == c.PedidoId);
    Assert.Equal(c.B, perda.EstruturaItemId);
    Assert.Null(perda.AgrupamentoId);
    Assert.False(perda.Tudo);
    var linhaNoLivro = await db.Movimentacoes.AsNoTracking().SingleAsync(m => m.PerdaId == perda.Id);
    Assert.Equal(("Perda", "NoSetor", "Perdido"), (linhaNoLivro.Tipo, linhaNoLivro.OrigemPosicao, linhaNoLivro.DestinoPosicao));
  }

  [Fact]
  public async Task Baixa_de_parte_do_pai_tira_os_filhos_a_iniciar_na_mesma_Perda()
  {
    await using var c = await CriarAsync(_factory);

    var resposta = await c.Como(c.Pcp).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas",
        Parte("Descarte", Origem("AIniciar", null, null, 3m)));

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    // A deixa de precisar de 3: B (razao 2) perde 6 a iniciar, C (razao 1) perde 3 (C6).
    Assert.Equal([(c.A, "AIniciar", 3m), (c.B, "AIniciar", 6m), (c.C, "AIniciar", 3m)], Linhas(await CorpoAsync(resposta)));
    Assert.Equal(1, await PerdasDoPedidoAsync(c));
    Assert.Equal(6m, await SaldoAsync(c, c.B, "Perdido"));
    Assert.Equal(14m, await SaldoAsync(c, c.B, "AIniciar"));
    Assert.Equal(7m, await SaldoAsync(c, c.C, "AIniciar"));
  }

  [Fact]
  public async Task Baixa_tudo_de_uma_Peca_leva_a_arvore_e_conclui_a_Peca()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 4m);

    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas",
        new { motivo = "Descarte", tudo = true });

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var baixa = await CorpoAsync(resposta);
    Assert.True(baixa.GetProperty("tudo").GetBoolean());
    Assert.Equal(
        [(c.A, "AIniciar", 10m), (c.B, "AIniciar", 16m), (c.B, "NoSetor", 4m), (c.C, "AIniciar", 10m)],
        Linhas(baixa));
    Assert.Equal([("Perdido", (int?)null, (int?)null, 10m)], await SaldosAsync(c, c.A));
    Assert.Equal([("Perdido", (int?)null, (int?)null, 20m)], await SaldosAsync(c, c.B));

    // A Peca A e a unica do Pedido: perdida inteira, ela conclui, e o Pedido com ela (regra 13, C10).
    var pedido = await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/pedidos/{c.PedidoId}"));
    Assert.Equal("Concluido", pedido.GetProperty("status").GetString());
    Assert.Equal(JsonValueKind.String, pedido.GetProperty("dataConclusao").ValueKind);
  }

  [Fact]
  public async Task Previa_devolve_o_mesmo_que_a_baixa_grava_e_nao_grava()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 20m);
    var corpo = Parte("Descarte", Origem("AIniciar", null, null, 3m));
    var qualidade = c.Como(c.Qualidade);

    var previa = await qualidade.PostAsJsonAsync($"/api/estrutura/{c.A}/baixas/previa", corpo);

    Assert.Equal(HttpStatusCode.OK, previa.StatusCode);
    var doPrevisto = await CorpoAsync(previa);
    Assert.Equal(0, await PerdasDoPedidoAsync(c));
    Assert.Equal(0m, await SaldoAsync(c, c.A, "Perdido"));
    // B ja saiu todo de "a iniciar": nao perde nada, e as 6 que A deixa de pedir viram sobra (C6). C perde 3.
    Assert.Equal([(c.A, "AIniciar", 3m), (c.C, "AIniciar", 3m)], Linhas(doPrevisto));
    var sobra = Assert.Single(doPrevisto.GetProperty("viramSobra").EnumerateArray());
    Assert.Equal((c.B, "Suporte", 6m),
        (sobra.GetProperty("estruturaItemId").GetInt32(), sobra.GetProperty("descricao").GetString(), sobra.GetProperty("quantidade").GetDecimal()));
    Assert.Equal(JsonValueKind.Null, doPrevisto.GetProperty("travaNoTopo").ValueKind);
    var primeira = doPrevisto.GetProperty("linhas")[0];
    Assert.Equal("Chassi da Fase 3", primeira.GetProperty("descricao").GetString());
    Assert.StartsWith("f3-", primeira.GetProperty("codigo").GetString());

    var gravada = await qualidade.PostAsJsonAsync($"/api/estrutura/{c.A}/baixas", corpo);

    Assert.Equal(HttpStatusCode.Created, gravada.StatusCode);
    Assert.Equal(LinhasInteiras(doPrevisto), LinhasInteiras(await CorpoAsync(gravada)));
    Assert.Equal(1, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task IncluirTopo_grava_a_Peca_do_topo_na_mesma_Perda()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 20m);
    var corpo = new
    {
      motivo = "PerdaArmazem", tudo = false, origens = new[] { Origem("NoSetor", c.Corte, 1, 4m) }, incluirTopo = true,
    };
    var qualidade = c.Como(c.Qualidade);

    // Sem as 4 de B, A so monta 8 das 10: a previa avisa que trava 2 (secao 4.6).
    var previa = await CorpoAsync(await qualidade.PostAsJsonAsync($"/api/estrutura/{c.B}/baixas/previa", corpo with { incluirTopo = false }));
    var trava = previa.GetProperty("travaNoTopo");
    Assert.Equal((c.A, "Chassi da Fase 3", 2m, 10m),
        (trava.GetProperty("pecaId").GetInt32(), trava.GetProperty("descricao").GetString(),
         trava.GetProperty("quantidade").GetDecimal(), trava.GetProperty("aIniciar").GetDecimal()));
    Assert.Equal([(c.B, "NoSetor", 4m)], Linhas(previa));

    var resposta = await qualidade.PostAsJsonAsync($"/api/estrutura/{c.B}/baixas", corpo);

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    // As 2 de A saem de "a iniciar", e por C6 as 2 de C que elas pediam saem junto (decisao P10 do plano de backend da Fase 5B-1).
    Assert.Equal([(c.B, "NoSetor", 4m), (c.A, "AIniciar", 2m), (c.C, "AIniciar", 2m)], Linhas(await CorpoAsync(resposta)));
    Assert.Equal(1, await PerdasDoPedidoAsync(c));
    Assert.Equal(2m, await SaldoAsync(c, c.A, "Perdido"));
  }

  [Fact]
  public async Task IncluirTopo_de_um_neto_sobe_a_cadeia_ate_a_Peca_e_desce_pelo_outro_ramo()
  {
    // Tres niveis: um neto N copiado do catalogo (20, razao 1) sob B. A cadeia de N e [B, A]; C so se alcanca pela
    // arvore da Peca do topo, nao pela de B (secao 4.6 da spec da Fase 5B-1).
    await using var c = await CriarAsync(_factory);
    var criado = await c.Como(c.Pcp).PostAsJsonAsync($"/api/estrutura/{c.B}/filhos",
        new { componenteId = (int?)c.ComponenteId, descricao = (string?)null, quantidade = 20m, quantidadePorPai = 1m });
    await Garantir(criado);
    var neto = (await CorpoAsync(criado)).GetProperty("id").GetInt32();
    var corpo = new
    {
      motivo = "PerdaArmazem", tudo = false, origens = new[] { Origem("AIniciar", null, null, 4m) }, incluirTopo = true,
    };
    var qualidade = c.Como(c.Qualidade);

    // Sem 4 de N, B so monta 16 das 20, e A (razao 2 de B) so monta 8 das 10: trava 2 de A, pela razao de cada nivel.
    var previa = await CorpoAsync(await qualidade.PostAsJsonAsync($"/api/estrutura/{neto}/baixas/previa", corpo));
    var trava = previa.GetProperty("travaNoTopo");
    Assert.Equal((c.A, "Chassi da Fase 3", 2m, 10m),
        (trava.GetProperty("pecaId").GetInt32(), trava.GetProperty("descricao").GetString(),
         trava.GetProperty("quantidade").GetDecimal(), trava.GetProperty("aIniciar").GetDecimal()));

    var resposta = await qualidade.PostAsJsonAsync($"/api/estrutura/{neto}/baixas", corpo);

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    // As 2 de A saem de "a iniciar" e a cascata C6 desce da Peca pelos dois ramos: B deixa de precisar de 4, C de 2. N
    // ja perdeu as 4 que B deixa de pedir, entao nao perde mais nada.
    Assert.Equal(
        [(neto, "AIniciar", 4m), (c.A, "AIniciar", 2m), (c.B, "AIniciar", 4m), (c.C, "AIniciar", 2m)],
        Linhas(await CorpoAsync(resposta)));
    Assert.Equal(1, await PerdasDoPedidoAsync(c));
    Assert.Equal(2m, await SaldoAsync(c, c.C, "Perdido"));
  }

  [Fact]
  public async Task Tudo_de_Item_por_perda_com_IncluirTopo_grava_a_Peca_do_topo_na_mesma_Perda()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 6m);
    var qualidade = c.Como(c.Qualidade);

    // D14 da spec da Fase 5B-1: sem B nenhum, A nao monta nada, e a previa diz que trava as 10.
    var previa = await CorpoAsync(await qualidade.PostAsJsonAsync($"/api/estrutura/{c.B}/baixas/previa",
        new { motivo = "MortaEmProcesso", tudo = true }));
    Assert.Equal(10m, previa.GetProperty("travaNoTopo").GetProperty("quantidade").GetDecimal());

    var resposta = await qualidade.PostAsJsonAsync($"/api/estrutura/{c.B}/baixas",
        new { motivo = "MortaEmProcesso", tudo = true, incluirTopo = true });

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    Assert.Equal(
        [(c.B, "AIniciar", 14m), (c.B, "NoSetor", 6m), (c.A, "AIniciar", 10m), (c.C, "AIniciar", 10m)],
        Linhas(await CorpoAsync(resposta)));
    Assert.Equal(1, await PerdasDoPedidoAsync(c));
    Assert.Equal(10m, await SaldoAsync(c, c.A, "Perdido"));
  }

  [Fact]
  public async Task Tudo_de_Item_por_Descarte_com_IncluirTopo_e_OrigemInvalida()
  {
    await using var c = await CriarAsync(_factory);

    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.B}/baixas",
        new { motivo = "Descarte", tudo = true, incluirTopo = true });

    var (status, erro, _) = await RecusaAsync(resposta);
    Assert.Equal((HttpStatusCode.BadRequest, "OrigemInvalida"), (status, erro));
    Assert.Equal(0, await PerdasDoPedidoAsync(c));
  }

  public static TheoryData<string, string> CorposRecusadosNaEntrada => new()
  {
    { """{ "motivo": "Roubo", "tudo": true }""", "MotivoInvalido" },
    { """{ "tudo": true }""", "MotivoInvalido" },
    { """{ "motivo": "Descarte", "origens": [ { "posicao": "Montado", "quantidade": 1 } ] }""", "OrigemInvalida" },
    { """{ "motivo": "Descarte", "origens": [ { "posicao": "AguardandoLiberacao", "quantidade": 1 } ] }""", "OrigemInvalida" },
    { """{ "motivo": "Descarte", "origens": [ { "posicao": "AIniciar", "setorId": 1, "quantidade": 1 } ] }""", "OrigemInvalida" },
    { """{ "motivo": "Descarte", "tudo": true, "origens": [ { "posicao": "AIniciar", "quantidade": 1 } ] }""", "OrigemInvalida" },
    { """{ "motivo": "Descarte", "origens": [] }""", "OrigemInvalida" },
    { """{ "motivo": "Descarte", "origens": [ { "posicao": "AIniciar", "quantidade": 1 }, { "posicao": "AIniciar", "quantidade": 2 } ] }""", "ItemRepetido" },
    { """{ "motivo": "Descarte", "origens": [ { "posicao": "AIniciar", "quantidade": 0 } ] }""", "QuantidadeInvalida" },
    { """{ "motivo": "Descarte", "origens": [ { "posicao": "AIniciar", "quantidade": 0.00001 } ] }""", "QuantidadeInvalida" },
    { """{ "motivo": "Descarte", "incluirTopo": true, "origens": [ { "posicao": "AIniciar", "quantidade": 1 } ] }""", "OrigemInvalida" },
  };

  [Theory]
  [MemberData(nameof(CorposRecusadosNaEntrada))]
  public async Task Entrada_invalida_e_400_com_o_codigo_e_nada_grava(string json, string codigo)
  {
    await using var c = await CriarAsync(_factory);
    var qualidade = c.Como(c.Qualidade);

    foreach (var rota in new[] { $"/api/estrutura/{c.A}/baixas", $"/api/estrutura/{c.A}/baixas/previa" })
    {
      var resposta = await qualidade.PostAsync(rota, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
      var (status, erro, mensagem) = await RecusaAsync(resposta);
      Assert.Equal((HttpStatusCode.BadRequest, codigo), (status, erro));
      Assert.False(string.IsNullOrWhiteSpace(mensagem));
    }
    Assert.Equal(0, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task Observacao_acima_de_500_e_TextoLongoDemais()
  {
    await using var c = await CriarAsync(_factory);

    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas",
        new { motivo = "Descarte", observacao = new string('x', 501), tudo = true });

    var (status, erro, _) = await RecusaAsync(resposta);
    Assert.Equal((HttpStatusCode.BadRequest, "TextoLongoDemais"), (status, erro));
    // 500 cabe.
    var cabe = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas/previa",
        new { motivo = "Descarte", observacao = new string('x', 500), tudo = true });
    Assert.Equal(HttpStatusCode.OK, cabe.StatusCode);
  }

  [Fact]
  public async Task Origens_acima_do_teto_do_lote_e_LoteGrandeDemais()
  {
    await using var c = await CriarAsync(_factory);
    var origens = Enumerable.Range(1, 101).Select(i => Origem("NoSetor", c.Corte, i, 1m)).ToArray();

    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas", Parte("Descarte", origens));

    var (status, erro, _) = await RecusaAsync(resposta);
    Assert.Equal((HttpStatusCode.BadRequest, "LoteGrandeDemais"), (status, erro));
  }

  [Fact]
  public async Task Acima_do_saldo_da_origem_e_SaldoInsuficiente_nomeando_a_posicao()
  {
    await using var c = await CriarAsync(_factory);

    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas",
        Parte("PerdaArmazem", Origem("AIniciar", null, null, 11m)));

    var (status, erro, mensagem) = await RecusaAsync(resposta);
    Assert.Equal((HttpStatusCode.Conflict, "SaldoInsuficiente"), (status, erro));
    Assert.Equal("Só há 10 de Chassi da Fase 3 a iniciar.", mensagem);
    Assert.Equal(0, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task Tudo_sem_nada_em_producao_e_NadaEmProducao()
  {
    await using var c = await CriarAsync(_factory);
    var filho = await FilhoDoCatalogoAsync(c);
    var qualidade = c.Como(c.Qualidade);
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{filho}/baixas", new { motivo = "Descarte", tudo = true }));

    var resposta = await qualidade.PostAsJsonAsync($"/api/estrutura/{filho}/baixas", new { motivo = "Descarte", tudo = true });

    var (status, erro, mensagem) = await RecusaAsync(resposta);
    Assert.Equal((HttpStatusCode.Conflict, "NadaEmProducao"), (status, erro));
    Assert.Equal("Chassi da Fase 3 não tem nada em produção para dar baixa.", mensagem);
    Assert.Equal(1, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task Pedido_Cancelado_e_PedidoFechado()
  {
    await using var c = await CriarAsync(_factory);
    using (var escopo = _factory.Services.CreateScope())
    {
      // Nenhuma rota leva a `Cancelado` (C20 da spec da Fase 5B-1): o status vai direto no banco.
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Pedido SET Status = 'Cancelado' WHERE Id = {c.PedidoId}");
    }

    foreach (var rota in new[] { $"/api/estrutura/{c.B}/baixas", $"/api/estrutura/{c.B}/baixas/previa" })
    {
      var resposta = await c.Como(c.Qualidade).PostAsJsonAsync(rota, new { motivo = "Descarte", tudo = true });
      var (status, erro, _) = await RecusaAsync(resposta);
      Assert.Equal((HttpStatusCode.Conflict, "PedidoFechado"), (status, erro));
    }
    Assert.Equal(0, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task No_inexistente_e_404()
  {
    await using var c = await CriarAsync(_factory);

    foreach (var rota in new[] { "/api/estrutura/999999999/baixas", "/api/estrutura/999999999/baixas/previa" })
    {
      var resposta = await c.Como(c.Qualidade).PostAsJsonAsync(rota, new { motivo = "Descarte", tudo = true });
      Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
      // 404 sem codigo no corpo, como o da execucao (`ExecucaoControllerBase.Recusar`).
      Assert.DoesNotContain("\"erro\"", await resposta.Content.ReadAsStringAsync());
    }
    // A rota existe: o 404 acima e do no, nao da rota.
    Assert.Equal(HttpStatusCode.OK,
        (await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas/previa", new { motivo = "Descarte", tudo = true })).StatusCode);
  }

  [Fact]
  public async Task Operador_e_Movimentador_nao_dao_baixa()
  {
    await using var c = await CriarAsync(_factory);

    foreach (var quem in new[] { c.Operador, c.Movimentador })
      foreach (var rota in new[] { $"/api/estrutura/{c.A}/baixas", $"/api/estrutura/{c.A}/baixas/previa" })
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.Como(quem).PostAsJsonAsync(rota, new { motivo = "Descarte", tudo = true })).StatusCode);

    // O PCP da baixa (D5).
    Assert.Equal(HttpStatusCode.OK,
        (await c.Como(c.Pcp).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas/previa", new { motivo = "Descarte", tudo = true })).StatusCode);
    Assert.Equal(0, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task Estorno_generico_recusa_linha_de_baixa()
  {
    await using var c = await CriarAsync(_factory);
    var gravada = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.C}/baixas",
        Parte("Descarte", Origem("AIniciar", null, null, 1m)));
    await Garantir(gravada);
    var baixaId = (await CorpoAsync(gravada)).GetProperty("id").GetInt32();
    int linhaId;
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      linhaId = await db.Movimentacoes.AsNoTracking().Where(m => m.PerdaId == baixaId).Select(m => m.Id).SingleAsync();
    }

    var resposta = await c.Como(c.Pcp).PostAsync($"/api/movimentacoes/{linhaId}/estorno", null);

    var (status, erro, mensagem) = await RecusaAsync(resposta);
    Assert.Equal((HttpStatusCode.Conflict, "EstornoImpossivel"), (status, erro));
    Assert.Equal($"Esta linha faz parte da baixa {baixaId}: estorne a baixa inteira.", mensagem);
    Assert.Equal(1m, await SaldoAsync(c, c.C, "Perdido"));
  }

  /// <summary>As linhas do livro do no, por `GET /estrutura/{id}/movimentacoes`.</summary>
  private static async Task<List<JsonElement>> LivroDoNoAsync(CenarioDaFase3NaApi c, int no) =>
      (await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/estrutura/{no}/movimentacoes")))
          .GetProperty("movimentacoes").EnumerateArray().ToList();

  private static int? InteiroOuNulo(JsonElement linha, string campo) =>
      linha.GetProperty(campo).ValueKind == JsonValueKind.Null ? null : linha.GetProperty(campo).GetInt32();

  [Fact]
  public async Task Livro_do_no_traz_perdaId_e_motivoDaBaixa_na_linha_Perda_e_nulos_nas_demais()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 5m);
    var gravada = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.B}/baixas",
        Parte("PerdaArmazem", Origem("NoSetor", c.Corte, 1, 2m)));
    await Garantir(gravada);
    var baixaId = (await CorpoAsync(gravada)).GetProperty("id").GetInt32();

    var livro = await LivroDoNoAsync(c, c.B);

    var perda = Assert.Single(livro, l => l.GetProperty("tipo").GetString() == "Perda");
    Assert.Equal(baixaId, InteiroOuNulo(perda, "perdaId"));
    Assert.Equal("PerdaArmazem", perda.GetProperty("motivoDaBaixa").GetString());
    var inicio = Assert.Single(livro, l => l.GetProperty("tipo").GetString() == "Inicio");
    Assert.Null(InteiroOuNulo(inicio, "perdaId"));
    Assert.Equal(JsonValueKind.Null, inicio.GetProperty("motivoDaBaixa").ValueKind);
  }

  [Fact]
  public async Task Livro_do_no_traz_perdaId_e_motivoDaBaixa_tambem_no_Estorno_da_baixa()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 5m);
    var gravada = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.B}/baixas",
        Parte("MortaEmProcesso", Origem("NoSetor", c.Corte, 1, 2m)));
    await Garantir(gravada);
    var baixaId = (await CorpoAsync(gravada)).GetProperty("id").GetInt32();
    await Garantir(await EstornarAsync(c, c.Qualidade, baixaId));

    var livro = await LivroDoNoAsync(c, c.B);

    var estorno = Assert.Single(livro, l => l.GetProperty("tipo").GetString() == "Estorno");
    Assert.Equal(baixaId, InteiroOuNulo(estorno, "perdaId"));
    Assert.Equal("MortaEmProcesso", estorno.GetProperty("motivoDaBaixa").GetString());
  }

  [Fact]
  public async Task Duas_baixas_simultaneas_da_mesma_posicao_uma_passa_outra_e_SaldoInsuficiente()
  {
    // 3 de B no Corte, duas baixas de 2 ao mesmo tempo: uma passa e a outra e recusada.
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 3m);
    var corpo = Parte("MortaEmProcesso", Origem("NoSetor", c.Corte, 1, 2m));

    var respostas = await Task.WhenAll(
        c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.B}/baixas", corpo),
        c.Como(c.Pcp).PostAsJsonAsync($"/api/estrutura/{c.B}/baixas", corpo));

    Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Created);
    var recusada = Assert.Single(respostas, r => r.StatusCode != HttpStatusCode.Created);
    var (status, erro, _) = await RecusaAsync(recusada);
    Assert.Equal((HttpStatusCode.Conflict, "SaldoInsuficiente"), (status, erro));
    Assert.Equal(2m, await SaldoAsync(c, c.B, "Perdido"));
    Assert.Equal(1m, await SaldoAsync(c, c.B, "NoSetor"));
    Assert.Equal(1, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task Baixa_e_inicio_do_pai_simultaneos_nao_consomem_o_mesmo_a_iniciar()
  {
    // A tem 10 a iniciar e os filhos para montar as 10 na Solda. A baixa tira 6 de "a iniciar" e o
    // inicio consome 6 dali: as duas juntas passariam das 10.
    await using var c = await CriarAsync(_factory);
    await LevarParaAMontagemAsync(c, c.B, c.Corte, 20m);
    await LevarParaAMontagemAsync(c, c.C, c.Dobra, 10m);

    var respostas = await Task.WhenAll(
        c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas", Parte("Descarte", Origem("AIniciar", null, null, 6m))),
        c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.A}/inicios", new { setorId = c.Solda, quantidade = 6m }));

    Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Created);
    var recusada = Assert.Single(respostas, r => r.StatusCode != HttpStatusCode.Created);
    var (status, erro, _) = await RecusaAsync(recusada);
    Assert.Equal((HttpStatusCode.Conflict, "SaldoInsuficiente"), (status, erro));

    Assert.Equal(4m, await SaldoAsync(c, c.A, "AIniciar"));
    var perdido = await SaldoAsync(c, c.A, "Perdido");
    var iniciado = await SaldoAsync(c, c.A, "NoSetor");
    Assert.Equal(6m, perdido + iniciado);
    Assert.True(perdido == 0m || iniciado == 0m);
    // O pai so consome os filhos das unidades que iniciou: nunca os das que a baixa tirou.
    Assert.Equal(2m * iniciado, await SaldoAsync(c, c.B, "Montado"));
    Assert.Equal(20m - 2m * iniciado, await SaldoAsync(c, c.B, "AguardandoMontagem"));
    Assert.Equal(iniciado, await SaldoAsync(c, c.C, "Montado"));
    Assert.Equal(10m - iniciado, await SaldoAsync(c, c.C, "AguardandoMontagem"));
  }

  // O "Descartar tudo" do Agrupamento e do Pedido (P6 do plano de backend da Fase 5B-1).

  /// <summary>
  /// Uma Peca nova no Agrupamento do cenario, copiada do catalogo (sem descricao propria), no Corte, com
  /// `quantidade` unidades levadas ate o local de expedicao; `expedir` as remete todas.
  /// </summary>
  private static async Task<int> PecaNaExpedicaoAsync(
      CenarioDaFase3NaApi c, decimal quantidade, bool expedir = false, int? componenteId = null)
  {
    var pcp = c.Como(c.Pcp);
    var criada = await pcp.PostAsJsonAsync($"/api/agrupamentos/{c.AgrupamentoId}/estrutura",
        new { componenteId = componenteId ?? c.ComponenteId, quantidade, requerRelatorioDimensional = true });
    await Garantir(criada);
    var peca = (await CorpoAsync(criada)).GetProperty("id").GetInt32();
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{peca}/roteiro", new { passos = new[] { c.Corte } }));
    await LevarParaAMontagemAsync(c, peca, c.Corte, quantidade);
    if (expedir)
      await Garantir(await c.Como(c.Qualidade).PostAsJsonAsync("/api/remessas",
          new { itens = new[] { new { estruturaItemId = peca, quantidade } } }));
    return peca;
  }

  /// <summary>Uma Peca com `unidades` aguardando liberacao: nao conforme, contada e pedida a liberacao.</summary>
  private static async Task PecaAguardandoLiberacaoAsync(CenarioDaFase3NaApi c, decimal unidades, int? componenteId = null)
  {
    var peca = await PecaNaExpedicaoAsync(c, 6m, componenteId: componenteId);
    var qualidade = c.Como(c.Qualidade);
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/afericoes", new { resultado = "NaoConforme" }));
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/contagens", new { conformes = 6m - unidades, naoConformes = unidades }));
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/pedidos-de-liberacao",
        new { quantidade = unidades, justificativa = "A peça serve assim." }));
  }

  /// <summary>Um segundo Agrupamento no Pedido do cenario, com uma Peca a iniciar; sai no `DisposeAsync`.</summary>
  private sealed class OutroAgrupamento(WebApplicationFactory<Program> factory, int id, int peca) : IAsyncDisposable
  {
    public int Id { get; } = id;
    public int Peca { get; } = peca;

    public async ValueTask DisposeAsync()
    {
      using var escopo = factory.Services.CreateScope();
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      var ag = Id;
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.Movimentacao WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.EstruturaRoteiro WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag}");
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Agrupamento WHERE Id = {ag}");
    }
  }

  private async Task<OutroAgrupamento> OutroAgrupamentoAsync(CenarioDaFase3NaApi c)
  {
    var pcp = c.Como(c.Pcp);
    var criado = await pcp.PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/agrupamentos", new { codigo = "AG-02", tipo = "Avulso" });
    await Garantir(criado);
    var id = (await CorpoAsync(criado)).GetProperty("id").GetInt32();
    var outro = new OutroAgrupamento(_factory, id, 0);
    try
    {
      var peca = await pcp.PostAsJsonAsync($"/api/agrupamentos/{id}/estrutura",
          new { componenteId = c.ComponenteId, quantidade = 7m, requerRelatorioDimensional = false });
      await Garantir(peca);
      return new OutroAgrupamento(_factory, id, (await CorpoAsync(peca)).GetProperty("id").GetInt32());
    }
    catch
    {
      await outro.DisposeAsync();
      throw;
    }
  }

  /// <summary>
  /// Um segundo Componente do catalogo, de outra descricao, para a Peca que precisa de nome proprio na frase. Declare-o
  /// ANTES do cenario: ele sai por ultimo, depois dos nos que o apontam.
  /// </summary>
  private sealed class OutroComponente(WebApplicationFactory<Program> factory) : IAsyncDisposable
  {
    public int Id { get; private set; }
    private int _arquivoId;

    /// <summary>Com solido proprio (regra 18), do `admin` do seed: o autor sobrevive ao cenario, que apaga os usuarios dele antes.</summary>
    public async Task CriarAsync(string descricao)
    {
      using var escopo = factory.Services.CreateScope();
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      var arquivo = new Rastreamento.Domain.Entities.ArquivoDeComponente
      {
        NomeOriginal = "cubo.stl", Conteudo = StlDeTesteDaApi.CuboBinario(), CriadoPorUsuarioId = 1,
      };
      db.ArquivosDeComponente.Add(arquivo);
      await db.SaveChangesAsync();
      _arquivoId = arquivo.Id;
      var componente = new Rastreamento.Domain.Entities.Componente
      {
        Codigo = $"f3b-{Guid.NewGuid():N}"[..16], Descricao = descricao, Tipo = "Fabricado", Ativo = true,
        ArquivoSolidoId = arquivo.Id,
      };
      db.Componentes.Add(componente);
      await db.SaveChangesAsync();
      Id = componente.Id;
    }

    public async ValueTask DisposeAsync()
    {
      if (Id == 0) return;
      using var escopo = factory.Services.CreateScope();
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Componente WHERE Id = {Id}");
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.ArquivoDeComponente WHERE Id = {_arquivoId}");
    }
  }

  private async Task<(string? Status, bool Concluido)> StatusDoPedidoAsync(CenarioDaFase3NaApi c)
  {
    var pedido = await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/pedidos/{c.PedidoId}"));
    return (pedido.GetProperty("status").GetString(), pedido.GetProperty("dataConclusao").ValueKind == JsonValueKind.String);
  }

  [Fact]
  public async Task Descartar_tudo_do_Pedido_leva_todas_as_Pecas_numa_Perda_e_conclui_o_Pedido()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 4m);
    var expedida = await PecaNaExpedicaoAsync(c, 5m, expedir: true);
    var naExpedicao = await PecaNaExpedicaoAsync(c, 6m);

    var resposta = await c.Como(c.Pcp).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/descarte", new { observacao = "  Cliente cancelou  " });

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var baixa = await CorpoAsync(resposta);
    Assert.Equal("Descarte", baixa.GetProperty("motivo").GetString());
    Assert.Equal("Cliente cancelou", baixa.GetProperty("observacao").GetString());
    Assert.Equal("Pedido", baixa.GetProperty("escopo").GetProperty("tipo").GetString());
    Assert.Equal(c.PedidoId, baixa.GetProperty("escopo").GetProperty("id").GetInt32());
    Assert.True(baixa.GetProperty("tudo").GetBoolean());
    // A Peca toda expedida nao gera linha; as outras saem de todas as posicoes em que tinham algo.
    Assert.Equal(
        [(c.A, "AIniciar", 10m), (c.B, "AIniciar", 16m), (c.B, "NoSetor", 4m), (c.C, "AIniciar", 10m),
            (naExpedicao, "NaExpedicao", 6m)],
        Linhas(baixa).OrderBy(l => l.No).ThenBy(l => l.Origem, StringComparer.Ordinal).ToList());
    Assert.DoesNotContain(Linhas(baixa), l => l.No == expedida);
    Assert.Equal(1, await PerdasDoPedidoAsync(c));
    Assert.Equal(6m, await SaldoAsync(c, naExpedicao, "Perdido"));
    Assert.Equal(5m, await SaldoAsync(c, expedida, "Expedido"));

    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      var perda = await db.Perdas.AsNoTracking().SingleAsync(p => p.PedidoId == c.PedidoId);
      Assert.True(perda.Tudo);
      Assert.Equal("Descarte", perda.Motivo);
      Assert.Null(perda.AgrupamentoId);
      Assert.Null(perda.EstruturaItemId);
    }
    Assert.Equal(("Concluido", true), await StatusDoPedidoAsync(c));
  }

  [Fact]
  public async Task Descartar_tudo_do_Agrupamento_conclui_o_Agrupamento_e_nao_mexe_no_outro()
  {
    await using var c = await CriarAsync(_factory);
    await using var outro = await OutroAgrupamentoAsync(c);

    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/agrupamentos/{c.AgrupamentoId}/descarte", new { });

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var baixa = await CorpoAsync(resposta);
    Assert.Equal("Agrupamento", baixa.GetProperty("escopo").GetProperty("tipo").GetString());
    Assert.Equal(c.AgrupamentoId, baixa.GetProperty("escopo").GetProperty("id").GetInt32());
    Assert.Equal([c.A, c.B, c.C], Linhas(baixa).Select(l => l.No).Distinct().ToList());

    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var perda = await db.Perdas.AsNoTracking().SingleAsync(p => p.PedidoId == c.PedidoId);
    Assert.Equal(c.AgrupamentoId, perda.AgrupamentoId);
    Assert.Null(perda.EstruturaItemId);
    Assert.NotNull((await db.Agrupamentos.AsNoTracking().SingleAsync(a => a.Id == c.AgrupamentoId)).DataConclusao);
    Assert.Null((await db.Agrupamentos.AsNoTracking().SingleAsync(a => a.Id == outro.Id)).DataConclusao);
    // O outro Agrupamento segue com as 7 a iniciar, e o Pedido segue aberto.
    var intactos = await db.Movimentacoes.AsNoTracking().CountAsync(m => m.EstruturaItemId == outro.Peca);
    Assert.Equal(0, intactos);
    Assert.False((await StatusDoPedidoAsync(c)).Concluido);
  }

  [Fact]
  public async Task Descartar_tudo_de_Pedido_Aberto_conclui_o_Pedido()
  {
    await using var c = await CriarAsync(_factory);
    Assert.Equal(("Aberto", false), await StatusDoPedidoAsync(c));

    var resposta = await c.Como(c.Pcp).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/descarte", new { });

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    Assert.Equal(("Concluido", true), await StatusDoPedidoAsync(c));
  }

  [Fact]
  public async Task Descartar_tudo_com_aguardando_liberacao_e_LiberacaoEmAberto_nomeando_a_Peca()
  {
    await using var outroComponente = new OutroComponente(_factory);
    await using var c = await CriarAsync(_factory);
    await outroComponente.CriarAsync("Suporte do descarte");
    await PecaAguardandoLiberacaoAsync(c, 2m);
    await PecaAguardandoLiberacaoAsync(c, 1m, outroComponente.Id);

    foreach (var rota in new[]
    {
      $"/api/pedidos/{c.PedidoId}/descarte", $"/api/pedidos/{c.PedidoId}/descarte/previa",
      $"/api/agrupamentos/{c.AgrupamentoId}/descarte", $"/api/agrupamentos/{c.AgrupamentoId}/descarte/previa",
    })
    {
      var (status, erro, mensagem) = await RecusaAsync(await c.Como(c.Qualidade).PostAsJsonAsync(rota, new { }));
      Assert.Equal((HttpStatusCode.Conflict, "LiberacaoEmAberto"), (status, erro));
      // Todas as Pecas presas na mesma recusa (C15), cada uma pelo nome do seu Componente (regra 19).
      Assert.Equal(
          "Há unidades aguardando liberação em Chassi da Fase 3, Suporte do descarte: "
          + "estorne os pedidos de liberação antes de descartar tudo.", mensagem);
    }
    Assert.Equal(0, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task Descartar_tudo_sem_nada_em_producao_e_NadaEmProducao()
  {
    await using var c = await CriarAsync(_factory);
    await using var outro = await OutroAgrupamentoAsync(c);
    var qualidade = c.Como(c.Qualidade);
    await Garantir(await qualidade.PostAsJsonAsync($"/api/agrupamentos/{c.AgrupamentoId}/descarte", new { }));

    foreach (var rota in new[] { $"/api/agrupamentos/{c.AgrupamentoId}/descarte", $"/api/agrupamentos/{c.AgrupamentoId}/descarte/previa" })
    {
      var (status, erro, mensagem) = await RecusaAsync(await qualidade.PostAsJsonAsync(rota, new { }));
      Assert.Equal((HttpStatusCode.Conflict, "NadaEmProducao"), (status, erro));
      Assert.Equal("O Agrupamento não tem nada em produção para descartar.", mensagem);
    }
    Assert.Equal(1, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task Descartar_tudo_de_Pedido_sem_nenhuma_Peca_e_NadaEmProducao()
  {
    await using var c = await CriarAsync(_factory);
    var vazio = await c.Como(c.Pcp).PostAsJsonAsync("/api/pedidos",
        new { numero = $"f3v-{Guid.NewGuid():N}"[..14], cliente = "Cliente F3", dataEntrega = "2099-12-31" });
    await Garantir(vazio);
    var pedidoVazio = (await CorpoAsync(vazio)).GetProperty("id").GetInt32();
    try
    {
      foreach (var rota in new[] { $"/api/pedidos/{pedidoVazio}/descarte", $"/api/pedidos/{pedidoVazio}/descarte/previa" })
      {
        var (status, erro, mensagem) = await RecusaAsync(await c.Como(c.Qualidade).PostAsJsonAsync(rota, new { }));
        Assert.Equal((HttpStatusCode.Conflict, "NadaEmProducao"), (status, erro));
        Assert.Equal("O Pedido não tem nada em produção para descartar.", mensagem);
      }
    }
    finally
    {
      using var escopo = _factory.Services.CreateScope();
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Pedido WHERE Id = {pedidoVazio}");
    }
  }

  [Fact]
  public async Task Previa_do_descarte_do_Pedido_bate_com_o_gravado()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 4m);
    await PecaNaExpedicaoAsync(c, 6m);
    var qualidade = c.Como(c.Qualidade);

    var previa = await qualidade.PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/descarte/previa", new { });

    Assert.Equal(HttpStatusCode.OK, previa.StatusCode);
    Assert.Equal(0, await PerdasDoPedidoAsync(c));
    var daPrevia = await CorpoAsync(previa);
    Assert.Empty(daPrevia.GetProperty("viramSobra").EnumerateArray());
    Assert.Equal(JsonValueKind.Null, daPrevia.GetProperty("travaNoTopo").ValueKind);

    var gravada = await qualidade.PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/descarte", new { });
    Assert.Equal(HttpStatusCode.Created, gravada.StatusCode);
    Assert.Equal(LinhasInteiras(daPrevia), LinhasInteiras(await CorpoAsync(gravada)));
  }

  [Fact]
  public async Task Pedido_pausado_aceita_baixa_e_descarte()
  {
    await using var c = await CriarAsync(_factory);
    await Garantir(await c.Como(c.Gestao).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/pausas", new { motivo = "outro pedido urgente" }));
    var qualidade = c.Como(c.Qualidade);

    var parte = await qualidade.PostAsJsonAsync($"/api/estrutura/{c.B}/baixas", Parte("PerdaArmazem", Origem("AIniciar", null, null, 2m)));
    var tudo = await qualidade.PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/descarte", new { });

    Assert.Equal(HttpStatusCode.Created, parte.StatusCode);
    Assert.Equal(HttpStatusCode.Created, tudo.StatusCode);
    Assert.Equal(2, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task Descarte_com_observacao_acima_de_500_e_agrupamento_ou_pedido_inexistente_recusam()
  {
    await using var c = await CriarAsync(_factory);
    var qualidade = c.Como(c.Qualidade);

    var longa = await qualidade.PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/descarte", new { observacao = new string('x', 501) });
    var (status, erro, _) = await RecusaAsync(longa);
    Assert.Equal((HttpStatusCode.BadRequest, "TextoLongoDemais"), (status, erro));

    foreach (var rota in new[]
    {
      "/api/agrupamentos/999999999/descarte", "/api/agrupamentos/999999999/descarte/previa",
      "/api/pedidos/999999999/descarte", "/api/pedidos/999999999/descarte/previa",
    })
      Assert.Equal(HttpStatusCode.NotFound, (await qualidade.PostAsJsonAsync(rota, new { })).StatusCode);

    Assert.Equal(HttpStatusCode.Forbidden,
        (await c.Como(c.Operador).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/descarte", new { })).StatusCode);
    Assert.Equal(0, await PerdasDoPedidoAsync(c));
  }

  // O estorno inteiro da baixa e as leituras (secoes 4.9 e 5.1 a 5.4 da spec da Fase 5B-1).

  private static async Task<int> DarBaixaDeParteAsync(
      CenarioDaFase3NaApi c, UsuarioDeTeste quem, int no, string motivo, decimal aIniciar)
  {
    var resposta = await c.Como(quem).PostAsJsonAsync($"/api/estrutura/{no}/baixas",
        Parte(motivo, Origem("AIniciar", null, null, aIniciar)));
    await Garantir(resposta);
    return (await CorpoAsync(resposta)).GetProperty("id").GetInt32();
  }

  private static async Task<int> DescartarOPedidoAsync(CenarioDaFase3NaApi c, UsuarioDeTeste quem)
  {
    var resposta = await c.Como(quem).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/descarte", new { });
    await Garantir(resposta);
    return (await CorpoAsync(resposta)).GetProperty("id").GetInt32();
  }

  private static Task<HttpResponseMessage> EstornarAsync(CenarioDaFase3NaApi c, UsuarioDeTeste quem, int baixaId) =>
      c.Como(quem).PostAsync($"/api/baixas/{baixaId}/estorno", null);

  private static async Task<List<int>> IdsDasBaixasAsync(HttpResponseMessage resposta)
  {
    await Garantir(resposta);
    return (await CorpoAsync(resposta)).EnumerateArray().Select(b => b.GetProperty("id").GetInt32()).ToList();
  }

  [Fact]
  public async Task Estorno_devolve_cada_linha_a_origem_e_marca_estornada()
  {
    // Tres niveis (A, B, N sob B) e o lote dividido. A baixa de 3 de A tira 6 de B e 6 de N pela
    // cascata, e C 3; o estorno devolve os quatro a "a iniciar".
    await using var c = await CriarAsync(_factory);
    var criado = await c.Como(c.Pcp).PostAsJsonAsync($"/api/estrutura/{c.B}/filhos",
        new { componenteId = (int?)c.ComponenteId, descricao = (string?)null, quantidade = 20m, quantidadePorPai = 1m });
    await Garantir(criado);
    var neto = (await CorpoAsync(criado)).GetProperty("id").GetInt32();
    var baixaId = await DarBaixaDeParteAsync(c, c.Qualidade, c.A, "Descarte", 3m);
    Assert.Equal(6m, await SaldoAsync(c, neto, "Perdido"));

    var resposta = await EstornarAsync(c, c.Qualidade, baixaId);

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var estornos = (await CorpoAsync(resposta)).EnumerateArray().ToList();
    Assert.Equal(4, estornos.Count);
    Assert.All(estornos, e =>
    {
      Assert.Equal("Estorno", e.GetProperty("tipo").GetString());
      Assert.Equal("Perdido", e.GetProperty("origem").GetProperty("posicao").GetString());
      Assert.Equal("AIniciar", e.GetProperty("destino").GetProperty("posicao").GetString());
      Assert.Equal(c.Qualidade.Id, e.GetProperty("usuarioId").GetInt32());
    });
    Assert.Equal(
        [(c.A, 3m), (c.B, 6m), (c.C, 3m), (neto, 6m)],
        estornos.Select(e => (e.GetProperty("estruturaItemId").GetInt32(), e.GetProperty("quantidade").GetDecimal()))
            .OrderBy(x => x.Item1).ToList());
    Assert.Equal(10m, await SaldoAsync(c, c.A, "AIniciar"));
    Assert.Equal(20m, await SaldoAsync(c, c.B, "AIniciar"));
    Assert.Equal(10m, await SaldoAsync(c, c.C, "AIniciar"));
    Assert.Equal(20m, await SaldoAsync(c, neto, "AIniciar"));
    foreach (var no in new[] { c.A, c.B, c.C, neto }) Assert.Equal(0m, await SaldoAsync(c, no, "Perdido"));

    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var originais = await db.Movimentacoes.AsNoTracking()
        .Where(m => m.PerdaId == baixaId && m.Tipo == "Perda").Select(m => m.Id).ToListAsync();
    var apontados = await db.Movimentacoes.AsNoTracking()
        .Where(m => m.PerdaId == baixaId && m.Tipo == "Estorno").Select(m => m.EstornoDeId!.Value).ToListAsync();
    Assert.Equal(originais.Order(), apontados.Order());

    var lida = (await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/estrutura/{c.A}/baixas"))).EnumerateArray().Single();
    Assert.True(lida.GetProperty("estornada").GetBoolean());
    Assert.False(lida.GetProperty("estornavel").GetBoolean());
  }

  [Fact]
  public async Task Estorno_de_baixa_que_concluiu_reabre_o_Pedido()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 4m);
    var baixaId = (await CorpoAsync(await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas",
        new { motivo = "Descarte", tudo = true }))).GetProperty("id").GetInt32();
    Assert.Equal(("Concluido", true), await StatusDoPedidoAsync(c));

    var resposta = await EstornarAsync(c, c.Qualidade, baixaId);

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    // O Pedido teve inicio (o de B): reaberto, volta a producao e perde a data de conclusao (C12).
    Assert.Equal(("EmProducao", false), await StatusDoPedidoAsync(c));
    Assert.Equal(4m, await SaldoAsync(c, c.B, "NoSetor"));
    Assert.Equal(16m, await SaldoAsync(c, c.B, "AIniciar"));
  }

  [Fact]
  public async Task Estorno_do_descarte_de_Pedido_que_nunca_comecou_volta_a_Aberto()
  {
    await using var c = await CriarAsync(_factory);
    Assert.Equal(("Aberto", false), await StatusDoPedidoAsync(c));
    var baixaId = await DescartarOPedidoAsync(c, c.Pcp);
    Assert.Equal(("Concluido", true), await StatusDoPedidoAsync(c));

    var resposta = await EstornarAsync(c, c.Pcp, baixaId);

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    Assert.Equal(("Aberto", false), await StatusDoPedidoAsync(c));
  }

  [Fact]
  public async Task Estorno_do_descarte_de_Pedido_cujo_unico_inicio_foi_estornado_volta_a_Aberto()
  {
    // C23 da spec da Fase 5B-1: `ListarPedidosComInicioAsync` so conta o inicio liquido de estorno.
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 4m);
    var inicio = (await CorpoAsync(await c.Como(c.Operador).GetAsync($"/api/estrutura/{c.B}/movimentacoes")))
        .GetProperty("movimentacoes").EnumerateArray().Single(m => m.GetProperty("tipo").GetString() == "Inicio");
    await Garantir(await c.Como(c.Operador).PostAsync($"/api/movimentacoes/{inicio.GetProperty("id").GetInt32()}/estorno", null));
    var baixaId = await DescartarOPedidoAsync(c, c.Pcp);
    Assert.Equal(("Concluido", true), await StatusDoPedidoAsync(c));

    await Garantir(await EstornarAsync(c, c.Pcp, baixaId));

    Assert.Equal(("Aberto", false), await StatusDoPedidoAsync(c));
  }

  [Fact]
  public async Task Estorno_de_outro_autor_por_Qualidade_e_403()
  {
    await using var c = await CriarAsync(_factory);
    var baixaId = await DarBaixaDeParteAsync(c, c.Pcp, c.C, "Descarte", 1m);

    var resposta = await EstornarAsync(c, c.Qualidade, baixaId);

    var (status, erro, _) = await RecusaAsync(resposta);
    Assert.Equal((HttpStatusCode.Forbidden, "Proibido"), (status, erro));
    Assert.Equal(HttpStatusCode.Forbidden, (await EstornarAsync(c, c.Operador, baixaId)).StatusCode);
    Assert.Equal(1m, await SaldoAsync(c, c.C, "Perdido"));
  }

  [Fact]
  public async Task Estorno_por_PCP_de_baixa_alheia_passa()
  {
    await using var c = await CriarAsync(_factory);
    var baixaId = await DarBaixaDeParteAsync(c, c.Qualidade, c.C, "Descarte", 1m);

    var resposta = await EstornarAsync(c, c.Pcp, baixaId);

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    Assert.Equal(0m, await SaldoAsync(c, c.C, "Perdido"));
    // O estorno e do PCP que o fez, nao do autor da baixa.
    Assert.Equal(c.Pcp.Id, (await CorpoAsync(resposta)).EnumerateArray().Single().GetProperty("usuarioId").GetInt32());
  }

  [Fact]
  public async Task Estorno_de_baixa_inexistente_e_404_e_segundo_estorno_e_JaEstornado()
  {
    await using var c = await CriarAsync(_factory);
    Assert.Equal(HttpStatusCode.NotFound, (await EstornarAsync(c, c.Qualidade, 999999999)).StatusCode);
    var baixaId = await DarBaixaDeParteAsync(c, c.Qualidade, c.C, "Descarte", 1m);
    await Garantir(await EstornarAsync(c, c.Qualidade, baixaId));

    var (status, erro, _) = await RecusaAsync(await EstornarAsync(c, c.Qualidade, baixaId));

    Assert.Equal((HttpStatusCode.Conflict, "JaEstornado"), (status, erro));
    Assert.Equal(0m, await SaldoAsync(c, c.C, "Perdido"));
  }

  [Fact]
  public async Task Estorno_em_Pedido_Cancelado_e_PedidoFechado()
  {
    await using var c = await CriarAsync(_factory);
    var baixaId = await DarBaixaDeParteAsync(c, c.Qualidade, c.C, "Descarte", 1m);
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Pedido SET Status = 'Cancelado' WHERE Id = {c.PedidoId}");
    }

    var (status, erro, _) = await RecusaAsync(await EstornarAsync(c, c.Qualidade, baixaId));

    Assert.Equal((HttpStatusCode.Conflict, "PedidoFechado"), (status, erro));
    Assert.Equal(1m, await SaldoAsync(c, c.C, "Perdido"));
  }

  [Fact]
  public async Task Dois_estornos_simultaneos_um_passa_outro_e_JaEstornado()
  {
    // Duas pessoas estornam a mesma baixa ao mesmo tempo: o indice unico do estorno fecha a corrida.
    await using var c = await CriarAsync(_factory);
    var baixaId = await DarBaixaDeParteAsync(c, c.Qualidade, c.A, "Descarte", 3m);

    var respostas = await Task.WhenAll(EstornarAsync(c, c.Qualidade, baixaId), EstornarAsync(c, c.Pcp, baixaId));

    Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
    var recusada = Assert.Single(respostas, r => r.StatusCode != HttpStatusCode.OK);
    var (status, erro, _) = await RecusaAsync(recusada);
    Assert.Equal((HttpStatusCode.Conflict, "JaEstornado"), (status, erro));
    Assert.Equal(0m, await SaldoAsync(c, c.A, "Perdido"));
    Assert.Equal(10m, await SaldoAsync(c, c.A, "AIniciar"));
    Assert.Equal(20m, await SaldoAsync(c, c.B, "AIniciar"));
    Assert.Equal(10m, await SaldoAsync(c, c.C, "AIniciar"));
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    Assert.Equal(3, await db.Movimentacoes.AsNoTracking().CountAsync(m => m.PerdaId == baixaId && m.Tipo == "Estorno"));
  }

  [Fact]
  public async Task Estorno_do_descarte_de_um_filho_volta_a_limitar_a_montagem_do_pai()
  {
    // Ramo "estornada" de `ListarSaidosDoProjetoAsync`: C descartado por inteiro deixa de
    // limitar a montagem de A; estornado o descarte, A volta a precisar de C. B esta toda na montagem da Solda.
    await using var c = await CriarAsync(_factory);
    await using var controle = await CriarAsync(_factory);
    foreach (var cenario in new[] { c, controle })
    {
      await LevarParaAMontagemAsync(cenario, cenario.B, cenario.Corte, 20m);
      var descartado = await cenario.Como(cenario.Qualidade).PostAsJsonAsync($"/api/estrutura/{cenario.C}/baixas",
          new { motivo = "Descarte", tudo = true });
      Assert.Equal(HttpStatusCode.Created, descartado.StatusCode);
    }
    var daBaixa = (await CorpoAsync(await c.Como(c.Pcp).GetAsync($"/api/estrutura/{c.C}/baixas"))).EnumerateArray().Single()
        .GetProperty("id").GetInt32();
    await Garantir(await EstornarAsync(c, c.Qualidade, daBaixa));

    // O controle (descarte mantido): A inicia so com B. O estornado: C volta a ser pedido e nao ha C na Solda.
    var seguiu = await controle.Como(controle.Operador).PostAsJsonAsync(
        $"/api/estrutura/{controle.A}/inicios", new { setorId = controle.Solda, quantidade = 10m });
    var travou = await c.Como(c.Operador).PostAsJsonAsync(
        $"/api/estrutura/{c.A}/inicios", new { setorId = c.Solda, quantidade = 10m });

    Assert.Equal(HttpStatusCode.Created, seguiu.StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, travou.StatusCode);
    Assert.Equal(0m, await SaldoAsync(c, c.A, "NoSetor"));
  }

  [Fact]
  public async Task Estorno_da_baixa_de_AIniciar_do_pai_devolve_o_que_falta_montar()
  {
    // Ramo "estorno" de `ListarPerdidosDeAIniciarAsync`: a baixa de 3 de A em "a iniciar"
    // baixa o que falta montar de 10 para 7, e o estorno o devolve a 10.
    await using var c = await CriarAsync(_factory);
    await LevarParaAMontagemAsync(c, c.B, c.Corte, 20m);

    async Task<decimal> FaltaMontarAsync()
    {
      var fila = await CorpoAsync(await c.Como(c.Operador).GetAsync($"/api/setores/{c.Solda}/fila"));
      return fila.GetProperty("aguardandoMontagem").EnumerateArray()
          .Single(m => m.GetProperty("pai").GetProperty("id").GetInt32() == c.A).GetProperty("faltaMontar").GetDecimal();
    }

    Assert.Equal(10m, await FaltaMontarAsync());
    var baixaId = await DarBaixaDeParteAsync(c, c.Qualidade, c.A, "Descarte", 3m);
    Assert.Equal(7m, await FaltaMontarAsync());

    await Garantir(await EstornarAsync(c, c.Qualidade, baixaId));

    Assert.Equal(10m, await FaltaMontarAsync());
  }

  [Fact]
  public async Task Leitura_do_no_traz_baixas_dele_e_dos_descendentes_e_a_do_Pedido()
  {
    await using var c = await CriarAsync(_factory);
    var deB = await DarBaixaDeParteAsync(c, c.Qualidade, c.B, "PerdaArmazem", 2m);
    var deC = await DarBaixaDeParteAsync(c, c.Pcp, c.C, "MortaEmProcesso", 1m);
    var doPedido = await DescartarOPedidoAsync(c, c.Pcp);
    var leitor = c.Como(c.Operador);

    // B: a propria e a do Pedido (que tirou o resto dele). C nao desce de B.
    Assert.Equal([doPedido, deB], await IdsDasBaixasAsync(await leitor.GetAsync($"/api/estrutura/{c.B}/baixas")));
    Assert.Equal([doPedido, deC], await IdsDasBaixasAsync(await leitor.GetAsync($"/api/estrutura/{c.C}/baixas")));
    // A: tem linha em B e em C pelos descendentes, e as do descarte.
    Assert.Equal([doPedido, deC, deB], await IdsDasBaixasAsync(await leitor.GetAsync($"/api/estrutura/{c.A}/baixas")));
    Assert.Equal(HttpStatusCode.NotFound, (await leitor.GetAsync("/api/estrutura/999999999/baixas")).StatusCode);
  }

  [Fact]
  public async Task Leitura_do_Pedido_traz_todas_mais_recente_primeiro()
  {
    await using var c = await CriarAsync(_factory);
    var primeira = await DarBaixaDeParteAsync(c, c.Qualidade, c.B, "PerdaArmazem", 2m);
    var segunda = await DarBaixaDeParteAsync(c, c.Pcp, c.C, "MortaEmProcesso", 1m);
    var terceira = await DescartarOPedidoAsync(c, c.Pcp);

    var resposta = await c.Como(c.Operador).GetAsync($"/api/pedidos/{c.PedidoId}/baixas");

    Assert.Equal([terceira, segunda, primeira], await IdsDasBaixasAsync(resposta));
    var corpo = (await CorpoAsync(await c.Como(c.Operador).GetAsync($"/api/pedidos/{c.PedidoId}/baixas"))).EnumerateArray().ToList();
    var daSegunda = corpo[1];
    Assert.Equal("MortaEmProcesso", daSegunda.GetProperty("motivo").GetString());
    Assert.Equal("No", daSegunda.GetProperty("escopo").GetProperty("tipo").GetString());
    Assert.Equal(c.C, daSegunda.GetProperty("escopo").GetProperty("id").GetInt32());
    Assert.Equal(c.Pcp.Id, daSegunda.GetProperty("usuarioId").GetInt32());
    Assert.Equal([(c.C, "AIniciar", 1m)], Linhas(daSegunda));
    Assert.Equal(HttpStatusCode.NotFound, (await c.Como(c.Operador).GetAsync("/api/pedidos/999999999/baixas")).StatusCode);
  }

  [Fact]
  public async Task Estornavel_falso_para_estornada_e_para_quem_nao_e_autor_nem_PCP()
  {
    await using var c = await CriarAsync(_factory);
    var baixaId = await DarBaixaDeParteAsync(c, c.Pcp, c.C, "Descarte", 1m);

    async Task<(bool Estornada, bool Estornavel)> LerAsync(UsuarioDeTeste quem)
    {
      var b = (await CorpoAsync(await c.Como(quem).GetAsync($"/api/pedidos/{c.PedidoId}/baixas"))).EnumerateArray().Single();
      return (b.GetProperty("estornada").GetBoolean(), b.GetProperty("estornavel").GetBoolean());
    }

    Assert.Equal((false, true), await LerAsync(c.Pcp));
    Assert.Equal((false, false), await LerAsync(c.Qualidade));
    Assert.Equal((false, false), await LerAsync(c.Operador));
    await Garantir(await EstornarAsync(c, c.Pcp, baixaId));
    Assert.Equal((true, false), await LerAsync(c.Pcp));
  }

  // Pedido concluido, sobra na Fila e editar a quantidade (C12, C24, C25 e D12 da spec da Fase 5B-1).

  /// <summary>
  /// Leva o Pedido do cenario a `Concluido` pelo caminho real: B inicia 4 no Corte e A leva uma baixa de tudo o que
  /// falta (10 "a iniciar", por Descarte). A cascata C6 tira o que B ainda tinha "a iniciar"; as 4 de B em
  /// producao ficam como sobra viva, com A toda perdida.
  /// </summary>
  private async Task ConcluirDeixandoSobraDeBAsync(
      CenarioDaFase3NaApi c, bool terminarAsQuatro = false, Func<Task>? antesDaBaixa = null)
  {
    await IniciarAsync(c, c.B, c.Corte, 4m);
    if (terminarAsQuatro)
      await Garantir(await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.B}/terminos",
          new { setorId = c.Corte, ordem = 1, quantidade = 4m }));
    if (antesDaBaixa is not null) await antesDaBaixa();
    await Garantir(await c.Como(c.Pcp).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas",
        Parte("Descarte", Origem("AIniciar", null, null, 10m))));
    Assert.Equal(("Concluido", true), await StatusDoPedidoAsync(c));
  }

  [Fact]
  public async Task Pedido_concluido_aceita_descarte_de_sobra_de_Item_em_qualquer_posicao()
  {
    await using var c = await CriarAsync(_factory);
    await ConcluirDeixandoSobraDeBAsync(c);
    Assert.Equal(4m, await SaldoAsync(c, c.B, "NoSetor"));
    var qualidade = c.Como(c.Qualidade);

    // De parte, de uma posicao em producao que nao e "a iniciar"; a previa aceita o mesmo.
    var parte = Parte("Descarte", Origem("NoSetor", c.Corte, 1, 1m));
    Assert.Equal(HttpStatusCode.OK, (await qualidade.PostAsJsonAsync($"/api/estrutura/{c.B}/baixas/previa", parte)).StatusCode);
    var umaParte = await qualidade.PostAsJsonAsync($"/api/estrutura/{c.B}/baixas", parte);
    Assert.Equal(HttpStatusCode.Created, umaParte.StatusCode);
    Assert.Equal(3m, await SaldoAsync(c, c.B, "NoSetor"));

    // De tudo: o resto da sobra.
    var tudo = await qualidade.PostAsJsonAsync($"/api/estrutura/{c.B}/baixas", new { motivo = "Descarte", tudo = true });
    Assert.Equal(HttpStatusCode.Created, tudo.StatusCode);
    Assert.Equal([(c.B, "NoSetor", 3m)], Linhas(await CorpoAsync(tudo)));
    Assert.Equal(20m, await SaldoAsync(c, c.B, "Perdido"));
    Assert.Equal(("Concluido", true), await StatusDoPedidoAsync(c));
  }

  [Fact]
  public async Task Pedido_concluido_recusa_perda_de_Item_com_PedidoFechado()
  {
    await using var c = await CriarAsync(_factory);
    await ConcluirDeixandoSobraDeBAsync(c);
    var antes = await PerdasDoPedidoAsync(c);

    foreach (var motivo in new[] { "PerdaArmazem", "MortaEmProcesso" })
    foreach (var rota in new[] { $"/api/estrutura/{c.B}/baixas", $"/api/estrutura/{c.B}/baixas/previa" })
    {
      var resposta = await c.Como(c.Qualidade).PostAsJsonAsync(rota, Parte(motivo, Origem("NoSetor", c.Corte, 1, 1m)));
      var (status, erro, mensagem) = await RecusaAsync(resposta);
      Assert.Equal((HttpStatusCode.Conflict, "PedidoFechado"), (status, erro));
      Assert.Contains("só se descarta a sobra de um Item", mensagem);
    }
    Assert.Equal(antes, await PerdasDoPedidoAsync(c));
    Assert.Equal(4m, await SaldoAsync(c, c.B, "NoSetor"));
  }

  [Fact]
  public async Task Pedido_concluido_recusa_baixa_de_Peca()
  {
    await using var c = await CriarAsync(_factory);
    await ConcluirDeixandoSobraDeBAsync(c);
    var antes = await PerdasDoPedidoAsync(c);

    foreach (var rota in new[] { $"/api/estrutura/{c.A}/baixas", $"/api/estrutura/{c.A}/baixas/previa" })
    {
      var resposta = await c.Como(c.Qualidade).PostAsJsonAsync(rota, new { motivo = "Descarte", tudo = true });
      var (status, erro, mensagem) = await RecusaAsync(resposta);
      Assert.Equal((HttpStatusCode.Conflict, "PedidoFechado"), (status, erro));
      Assert.Contains("uma Peça dele não tem nada em produção", mensagem);
    }
    Assert.Equal(antes, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task Descartar_tudo_de_Pedido_concluido_leva_so_a_sobra_dos_Itens()
  {
    await using var c = await CriarAsync(_factory);
    await ConcluirDeixandoSobraDeBAsync(c);
    var qualidade = c.Como(c.Qualidade);

    var previa = await qualidade.PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/descarte/previa", new { });
    var resposta = await qualidade.PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/descarte", new { });

    Assert.Equal(HttpStatusCode.OK, previa.StatusCode);
    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    Assert.Equal([(c.B, "NoSetor", 4m)], Linhas(await CorpoAsync(resposta)));
    Assert.Equal(20m, await SaldoAsync(c, c.B, "Perdido"));
    // Sem mais nada em producao, o segundo descarte e recusado como de costume.
    var (_, erro, _) = await RecusaAsync(await qualidade.PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/descarte", new { }));
    Assert.Equal("NadaEmProducao", erro);
  }

  [Fact]
  public async Task Fila_mostra_a_sobra_de_Pedido_concluido_so_na_secao_Sobra()
  {
    await using var c = await CriarAsync(_factory);
    await ConcluirDeixandoSobraDeBAsync(c, terminarAsQuatro: true);

    var fila = await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/setores/{c.Corte}/fila"));

    static bool DoNo(JsonElement l, int no, string campo = "no") => l.GetProperty(campo).GetProperty("id").GetInt32() == no;
    var sobra = fila.GetProperty("sobra").EnumerateArray().Where(l => DoNo(l, c.B)).ToList();
    Assert.Equal(4m, Assert.Single(sobra).GetProperty("quantidade").GetDecimal());
    Assert.Equal("UltimoPasso", sobra[0].GetProperty("origem").GetString());
    foreach (var secao in new[] { "aIniciar", "emTrabalho", "aguardandoColeta", "conserto" })
      Assert.DoesNotContain(fila.GetProperty(secao).EnumerateArray(), l => DoNo(l, c.B));
    Assert.DoesNotContain(fila.GetProperty("aguardandoMontagem").EnumerateArray(), l => DoNo(l, c.A, "pai"));
  }

  [Fact]
  public async Task Fila_nao_mostra_em_trabalho_nem_em_conserto_o_no_de_Pedido_concluido()
  {
    await using var c = await CriarAsync(_factory);

    static bool EmTrabalhoDe(JsonElement fila, int no) =>
        fila.GetProperty("emTrabalho").EnumerateArray().Any(l => l.GetProperty("no").GetProperty("id").GetInt32() == no);
    async Task<JsonElement> FilaDoCorteAsync() => await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/setores/{c.Corte}/fila"));

    // Controle positivo: com o Pedido rodando, as 4 de B no Corte (nao terminadas) estao em "Em trabalho". Sem ele o
    // `DoesNotContain` de depois passaria com a guarda de C25 desligada, que e o que este teste existe para pegar.
    var antes = false;
    await ConcluirDeixandoSobraDeBAsync(c, antesDaBaixa: async () => antes = EmTrabalhoDe(await FilaDoCorteAsync(), c.B));
    Assert.True(antes);
    Assert.Equal(("Concluido", true), await StatusDoPedidoAsync(c));

    var fila = await FilaDoCorteAsync();

    Assert.False(EmTrabalhoDe(fila, c.B));
    Assert.DoesNotContain(fila.GetProperty("conserto").EnumerateArray(), l => l.GetProperty("no").GetProperty("id").GetInt32() == c.B);
    // O saldo existe (nada se perdeu): so a Fila deixa de oferece-lo como trabalho em andamento (C25).
    Assert.Equal(4m, await SaldoAsync(c, c.B, "NoSetor"));
  }

  [Fact]
  public async Task Tarefas_nao_mostram_Pedido_concluido()
  {
    await using var c = await CriarAsync(_factory);

    static bool TemTarefaDe(JsonElement tarefas, int no) =>
        tarefas.GetProperty("grupos").EnumerateArray()
            .SelectMany(g => g.GetProperty("itens").EnumerateArray())
            .Any(t => t.GetProperty("no").GetProperty("id").GetInt32() == no);

    // Controle positivo: com o Pedido rodando, as 4 de B aguardando coleta sao tarefa do Movimentador. Sem isso o
    // `DoesNotContain` de depois passaria vazio, mesmo com a regra desligada.
    var antes = false;
    await ConcluirDeixandoSobraDeBAsync(c, terminarAsQuatro: true, antesDaBaixa: async () =>
        antes = TemTarefaDe(await CorpoAsync(await c.Como(c.Movimentador).GetAsync("/api/tarefas")), c.B));
    Assert.True(antes);
    Assert.Equal(("Concluido", true), await StatusDoPedidoAsync(c));

    var tarefas = await CorpoAsync(await c.Como(c.Movimentador).GetAsync("/api/tarefas"));

    Assert.False(TemTarefaDe(tarefas, c.B));
  }

  [Fact]
  public async Task Resumo_de_material_da_Fila_nao_mostra_no_descartado_por_inteiro()
  {
    // A (planejada com material) tem todo o B na montagem da Solda e uma segunda Peca em aberto, que segura o Pedido.
    // Descartada A por inteiro (10 "a iniciar"), o cartao de montagem continua (B aguarda), mas sem o resumo (C26).
    await using var c = await CriarAsync(_factory);
    var parafuso = await c.CriarMaterialAsync("PAR");
    await Garantir(await c.Como(c.Pcp).PutAsJsonAsync($"/api/estrutura/{c.A}/materiais",
        new { linhas = new[] { new { materialId = parafuso, quantidade = 10m } } }));
    await PecaNaExpedicaoAsync(c, 5m);
    await LevarParaAMontagemAsync(c, c.B, c.Corte, 20m);

    static JsonElement CartaoDeA(JsonElement fila, int a) =>
        fila.GetProperty("aguardandoMontagem").EnumerateArray().Single(m => m.GetProperty("pai").GetProperty("id").GetInt32() == a);
    var antes = CartaoDeA(await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/setores/{c.Solda}/fila")), c.A);
    Assert.NotEqual(JsonValueKind.Null, antes.GetProperty("material").ValueKind);

    await Garantir(await c.Como(c.Pcp).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas",
        Parte("Descarte", Origem("AIniciar", null, null, 10m))));

    var depois = CartaoDeA(await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/setores/{c.Solda}/fila")), c.A);
    Assert.Equal(JsonValueKind.Null, depois.GetProperty("material").ValueKind);
  }

  [Fact]
  public async Task Editar_quantidade_para_menos_com_Pedido_rodando_e_DiminuirPelaBaixa()
  {
    await using var c = await CriarAsync(_factory);
    var filho = await FilhoDoCatalogoAsync(c);
    await IniciarAsync(c, c.B, c.Corte, 4m);
    Assert.Equal(("EmProducao", false), await StatusDoPedidoAsync(c));

    var resposta = await c.Como(c.Pcp).PutAsJsonAsync($"/api/estrutura/{filho}",
        new { descricao = (string?)null, quantidade = 5m, quantidadePorPai = 1m });

    var (status, erro, mensagem) = await RecusaAsync(resposta);
    Assert.Equal((HttpStatusCode.Conflict, "DiminuirPelaBaixa"), (status, erro));
    Assert.Equal("Com o Pedido em produção, a quantidade de Chassi da Fase 3 só aumenta: para diminuir, use \"Dar baixa\".", mensagem);
    // A e C tambem tem 10: o que prova que o filho ficou em 10 e ler o no dele, pelo Id.
    var arvore = await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/agrupamentos/{c.AgrupamentoId}/estrutura"));
    static JsonElement? Achar(JsonElement nos, int id)
    {
      foreach (var no in nos.EnumerateArray())
      {
        if (no.GetProperty("id").GetInt32() == id) return no;
        if (Achar(no.GetProperty("filhos"), id) is JsonElement dentro) return dentro;
      }
      return null;
    }
    var doFilho = Achar(arvore, filho);
    Assert.NotNull(doFilho);
    Assert.Equal(10m, doFilho.Value.GetProperty("quantidade").GetDecimal());
  }

  [Fact]
  public async Task Editar_quantidade_para_mais_com_Pedido_rodando_passa()
  {
    await using var c = await CriarAsync(_factory);
    var filho = await FilhoDoCatalogoAsync(c);
    await IniciarAsync(c, c.B, c.Corte, 4m);

    var resposta = await c.Como(c.Pcp).PutAsJsonAsync($"/api/estrutura/{filho}",
        new { descricao = (string?)null, quantidade = 12m, quantidadePorPai = 1m });

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    Assert.Equal(12m, (await CorpoAsync(resposta)).GetProperty("quantidade").GetDecimal());
  }

  [Fact]
  public async Task Editar_quantidade_para_menos_com_Pedido_Aberto_passa()
  {
    await using var c = await CriarAsync(_factory);
    var filho = await FilhoDoCatalogoAsync(c);
    Assert.Equal(("Aberto", false), await StatusDoPedidoAsync(c));

    var resposta = await c.Como(c.Pcp).PutAsJsonAsync($"/api/estrutura/{filho}",
        new { descricao = (string?)null, quantidade = 5m, quantidadePorPai = 1m });

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    Assert.Equal(5m, (await CorpoAsync(resposta)).GetProperty("quantidade").GetDecimal());
  }
}
